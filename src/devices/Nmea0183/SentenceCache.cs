﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Iot.Device.Common;
using Iot.Device.Nmea0183.Sentences;
using UnitsNet;

namespace Iot.Device.Nmea0183
{
    /// <summary>
    /// Caches the last sentence(s) of each type and so provides a way of asking for the current position without waiting for the next message
    /// </summary>
    public sealed class SentenceCache : IDisposable
    {
        private readonly NmeaSinkAndSource _source;
        private readonly object _lock;
        private Dictionary<SentenceId, NmeaSentence> _sentences;
        private Queue<RoutePart> _lastRouteSentences;
        private Dictionary<string, Waypoint> _wayPoints;
        private Queue<SatellitesInView> _lastSatelliteInfos;

        private SentenceId[] _groupSentences = new SentenceId[]
        {
            // These sentences come in groups
            new SentenceId("GSV"),
            new SentenceId("RTE"),
            new SentenceId("WPL"),
        };

        /// <summary>
        /// Creates an new cache using the given source
        /// </summary>
        /// <param name="source">The source to monitor</param>
        public SentenceCache(NmeaSinkAndSource source)
        {
            _source = source;
            _lock = new object();
            _sentences = new Dictionary<SentenceId, NmeaSentence>();
            _lastRouteSentences = new Queue<RoutePart>();
            _lastSatelliteInfos = new Queue<SatellitesInView>();
            _wayPoints = new Dictionary<string, Waypoint>();
            StoreRawSentences = false;
            _source.OnNewSequence += OnNewSequence;
        }

        /// <summary>
        /// True to (also) store raw sentences. Otherwise only recognized decoded sentences are stored.
        /// Defaults to false.
        /// </summary>
        public bool StoreRawSentences
        {
            get;
            set;
        }

        /// <summary>
        /// Clears the cache
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _sentences.Clear();
                _lastRouteSentences.Clear();
                _wayPoints.Clear();
                _lastSatelliteInfos.Clear();
            }
        }

        /// <summary>
        /// Get the current position from the latest message containing any of the relevant data parts. This does not extrapolate the position
        /// if the last received message is old
        /// </summary>
        /// <param name="position">Current position</param>
        /// <param name="track">Track (course over ground)</param>
        /// <param name="sog">Speed over ground</param>
        /// <param name="heading">Vessel Heading</param>
        /// <returns>True if a valid position is returned</returns>
        public bool TryGetCurrentPosition(out GeographicPosition? position, out Angle track, out Speed sog, out Angle? heading)
        {
            return TryGetCurrentPosition(out position, false, out track, out sog, out heading);
        }

        /// <summary>
        /// Get the current position from the latest message containing any of the relevant data parts.
        /// If <paramref name="extrapolate"></paramref> is true, the speed and direction are used to extrapolate the position (many older
        /// GNSS receivers only deliver the position at 1Hz or less)
        /// </summary>
        /// <param name="position">Current position</param>
        /// <param name="extrapolate">True to extrapolate the current position using speed and track</param>
        /// <param name="track">Track (course over ground)</param>
        /// <param name="sog">Speed over ground</param>
        /// <param name="heading">Vessel Heading</param>
        /// <returns>True if a valid position is returned</returns>
        public bool TryGetCurrentPosition(out GeographicPosition? position, bool extrapolate, out Angle track, out Speed sog, out Angle? heading)
        {
            // Try to get any of the position messages
            var gll = (PositionFastUpdate?)GetLastSentence(PositionFastUpdate.Id);
            var gga = (GlobalPositioningSystemFixData?)GetLastSentence(GlobalPositioningSystemFixData.Id);
            var rmc = (RecommendedMinimumNavigationInformation?)GetLastSentence(RecommendedMinimumNavigationInformation.Id);
            var vtg = (TrackMadeGood?)GetLastSentence(TrackMadeGood.Id);
            var hdt = (HeadingTrue?)GetLastSentence(HeadingTrue.Id);
            TimeSpan age;

            List<(GeographicPosition, TimeSpan)> orderablePositions = new List<(GeographicPosition, TimeSpan)>();
            if (gll != null && gll.Position != null)
            {
                orderablePositions.Add((gll.Position, gll.Age));
            }

            if (gga != null && gga.Valid)
            {
                orderablePositions.Add((gga.Position, gga.Age));
            }

            if (rmc != null && rmc.Valid)
            {
                orderablePositions.Add((rmc.Position, rmc.Age));
            }

            if (orderablePositions.Count == 0)
            {
                // No valid positions received
                position = null;
                track = Angle.Zero;
                sog = Speed.Zero;
                heading = null;
                return false;
            }

            (position, age) = orderablePositions.OrderBy(x => x.Item2).Select(x => (x.Item1, x.Item2)).First();

            if (gga != null && gga.EllipsoidAltitude.HasValue)
            {
                // If we had seen a gga message, use its height, regardless of which other message provided the position
                position = new GeographicPosition(position.Latitude, position.Longitude, gga.EllipsoidAltitude.Value);
            }

            if (rmc != null)
            {
                sog = rmc.SpeedOverGround;
                track = rmc.TrackMadeGoodInDegreesTrue;
            }
            else if (vtg != null)
            {
                sog = vtg.Speed;
                track = vtg.CourseOverGroundTrue;
            }
            else
            {
                sog = Speed.Zero;
                track = Angle.Zero;
                heading = null;
                return false;
            }

            if (hdt != null)
            {
                heading = hdt.Angle;
            }
            else
            {
                heading = null;
            }

            if (extrapolate)
            {
                position = GreatCircle.CalcCoords(position, track, sog * age);
            }

            return true;
        }

        /// <summary>
        /// Gets the last sentence of the given type.
        /// Does not return sentences that are part of a group (i.e. GSV, RTE)
        /// </summary>
        /// <param name="id">Sentence Id to query</param>
        /// <returns>The last sentence of that type, or null.</returns>
        public NmeaSentence? GetLastSentence(SentenceId id)
        {
            lock (_lock)
            {
                if (_sentences.TryGetValue(id, out var sentence))
                {
                    return sentence;
                }

                return null;
            }
        }

        /// <summary>
        /// Tries to get a sentence of the given type
        /// </summary>
        /// <typeparam name="T">The type of the sentence to query</typeparam>
        /// <param name="id">The sentence id for T</param>
        /// <param name="sentence">Receives the sentence, if any was found</param>
        /// <returns>True on success, false if no such message was received</returns>
        public bool TryGetLastSentence<T>(SentenceId id,
#if NET5_0_OR_GREATER
            [NotNullWhen(true)]
#endif
            out T sentence)
            where T : NmeaSentence
        {
            var s = GetLastSentence(id);
            if (s is T)
            {
                sentence = (T)s;
                return true;
            }

            sentence = null!;
            return false;
        }

        /// <summary>
        /// Gets the last sentence of the given type.
        /// Does not return sentences that are part of a group (i.e. GSV, RTE)
        /// </summary>
        /// <param name="id">Sentence Id to query</param>
        /// <param name="maxAge">Maximum age of the sentence</param>
        /// <returns>The last sentence of that type, or null if none was received within the given timespan.</returns>
        public NmeaSentence? GetLastSentence(SentenceId id, TimeSpan maxAge)
        {
            lock (_lock)
            {
                if (_sentences.TryGetValue(id, out var sentence))
                {
                    if (sentence.Age < maxAge)
                    {
                        return sentence;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Returns the current route
        /// </summary>
        /// <param name="routeList">The list of points along the route</param>
        /// <returns>The state of the route received</returns>
        public AutopilotErrorState TryGetCurrentRoute(out List<RoutePoint> routeList)
        {
            routeList = new List<RoutePoint>();
            List<RoutePart>? segments = FindLatestCompleteRoute(out string routeName);
            if (segments == null)
            {
                return AutopilotErrorState.NoRoute;
            }

            List<string> wpNames = new List<string>();
            foreach (var segment in segments)
            {
                wpNames.AddRange(segment.WaypointNames);
            }

            // We've seen RTE messages, but no waypoints yet
            if (wpNames.Count == 0)
            {
                return AutopilotErrorState.WaypointsWithoutPosition;
            }

            if (wpNames.GroupBy(x => x).Any(g => g.Count() > 1))
            {
                return AutopilotErrorState.RouteWithDuplicateWaypoints;
            }

            for (var index = 0; index < wpNames.Count; index++)
            {
                var name = wpNames[index];
                GeographicPosition? position = null;
                if (_wayPoints.TryGetValue(name, out var pt))
                {
                    position = pt.Position;
                }
                else
                {
                    // Incomplete route - need to wait for all wpt messages
                    return AutopilotErrorState.WaypointsWithoutPosition;
                }

                RoutePoint rpt = new RoutePoint(routeName, index, wpNames.Count, name, position, null, null);
                routeList.Add(rpt);
            }

            return AutopilotErrorState.RoutePresent;
        }

        private List<RoutePart>? FindLatestCompleteRoute(out string routeName)
        {
            List<RoutePart> routeSentences;
            lock (_lock)
            {
                // Newest shall be first in list
                routeSentences = _lastRouteSentences.ToList();
                routeSentences.Reverse();
            }

            if (routeSentences.Count == 0)
            {
                routeName = "No route";
                return null;
            }

            routeName = string.Empty;
            RoutePart?[]? elements = null;

            // This is initially never 0 here
            while (routeSentences.Count > 0)
            {
                // Last initial sequence, take this as the header for what we combine
                var head = routeSentences.FirstOrDefault(x => x.Sequence == 1);
                if (head == null)
                {
                    routeName = "No complete route";
                    return null;
                }

                int numberOfSequences = head.TotalSequences;
                routeName = head.RouteName;

                elements = new RoutePart[numberOfSequences + 1]; // Use 1-based indexing
                bool complete = false;
                foreach (var sentence in routeSentences)
                {
                    if (sentence.RouteName == routeName && sentence.Sequence <= numberOfSequences)
                    {
                        // Iterate until we found one of each of the components of route
                        elements[sentence.Sequence] = sentence;
                        complete = true;
                        for (int i = 1; i <= numberOfSequences; i++)
                        {
                            if (elements[i] == null)
                            {
                                complete = false;
                            }
                        }

                        if (complete)
                        {
                            break;
                        }
                    }
                }

                if (complete)
                {
                    break;
                }

                // The sentence with the first header we found was incomplete - try the next (we're possibly just changing the route)
                routeSentences.RemoveRange(0, routeSentences.IndexOf(head) + 1);
            }

            List<RoutePart> ret = new List<RoutePart>();
            if (elements != null)
            {
                for (var index = 1; index < elements.Length; index++)
                {
                    var elem = elements[index];
                    if (elem == null)
                    {
                        // List is incomplete
                        return null;
                    }

                    ret.Add(elem);
                }
            }

            return ret.OrderBy(x => x.Sequence).ToList();
        }

        /// <summary>
        /// Returns the list of satellites in view
        /// </summary>
        /// <param name="totalNumberOfSatellites">Total number of satellites reported.
        /// This number might be larger than the number of elements in the list, as there might not be enough
        /// slots to transfer the whole status for all satellites</param>
        /// <returns></returns>
        public List<SatelliteInfo> GetSatellitesInView(out int totalNumberOfSatellites)
        {
            int maxSats = 0;
            List<SatellitesInView> sentences = new List<SatellitesInView>();
            lock (_lock)
            {
                sentences = _lastSatelliteInfos.ToList();
                sentences.Reverse();
            }

            List<SatellitesInView> filtered = new List<SatellitesInView>();
            foreach (var s in sentences)
            {
                // We might be getting satellite status from more than one source
                if (!filtered.Any(x => x.Sequence == s.Sequence && x.TalkerId == s.TalkerId))
                {
                    filtered.Add(s);
                }

                if (maxSats < s.TotalSatellites)
                {
                    maxSats = s.TotalSatellites;
                }
            }

            var allsats = filtered.Select(x => x.Satellites);

            List<SatelliteInfo> ret = new();
            foreach (var list1 in allsats)
            {
                foreach (var s2 in list1)
                {
                    if (ret.All(x => x.Id != s2.Id))
                    {
                        ret.Add(s2);
                    }
                }
            }

            if (maxSats < ret.Count)
            {
                maxSats = ret.Count;
            }

            ret = ret.OrderBy(x => x.Id).ToList();
            totalNumberOfSatellites = maxSats;
            return ret;
        }

        private void OnNewSequence(NmeaSinkAndSource? source, NmeaSentence sentence)
        {
            // Cache only valid sentences
            if (!sentence.Valid)
            {
                return;
            }

            if (!StoreRawSentences && sentence is RawSentence)
            {
                return;
            }

            lock (_lock)
            {
                if (!_groupSentences.Contains(sentence.SentenceId))
                {
                    // Standalone sequences. Only the last message needs to be kept
                    _sentences[sentence.SentenceId] = sentence;
                }
                else if (sentence.SentenceId == RoutePart.Id && (sentence is RoutePart rte))
                {
                    _lastRouteSentences.Enqueue(rte);
                    while (_lastRouteSentences.Count > 100)
                    {
                        // Throw away old entry
                        _lastRouteSentences.Dequeue();
                    }
                }
                else if (sentence.SentenceId == Waypoint.Id && (sentence is Waypoint wpt))
                {
                    // No reason to clean this up, this will never grow larger than a few hundred entries
                    _wayPoints[wpt.Name] = wpt;
                }
                else if (sentence.SentenceId == SatellitesInView.Id && (sentence is SatellitesInView gsv))
                {
                    _lastSatelliteInfos.Enqueue(gsv);
                    while (_lastSatelliteInfos.Count > 20)
                    {
                        // Throw away old entry
                        _lastSatelliteInfos.Dequeue();
                    }
                }
            }
        }

        /// <summary>
        /// Clean up everything
        /// </summary>
        public void Dispose()
        {
            _source.OnNewSequence -= OnNewSequence;
            _sentences.Clear();
        }

        /// <summary>
        /// Adds the given sentence to the cache - if manual filling is preferred
        /// </summary>
        /// <param name="sentence">Sentence to add</param>
        public void Add(NmeaSentence sentence)
        {
            OnNewSequence(null, sentence);
        }
    }
}
