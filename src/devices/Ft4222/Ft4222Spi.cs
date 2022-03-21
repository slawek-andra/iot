﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Device.Spi;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Iot.Device.FtCommon;

namespace Iot.Device.Ft4222
{
    /// <summary>
    /// Create a SPI Device based on FT4222 chipset
    /// </summary>
    public class Ft4222Spi : SpiDevice
    {
        private readonly SpiConnectionSettings _settings;
        private SafeFtHandle _ftHandle;

        /// <inheritdoc/>
        public override SpiConnectionSettings ConnectionSettings => _settings;

        /// <summary>
        /// Store the FTDI Device Information
        /// </summary>
        public Ft4222Device DeviceInformation { get; internal set; }

        /// <summary>
        /// Create an SPI FT4222 class
        /// </summary>
        /// <param name="settings">SPI Connection Settings</param>
        public Ft4222Spi(SpiConnectionSettings settings)
        {
            _settings = settings;
            // Check device
            var devInfos = Device.FtCommon.FtCommon.GetDevices();
            if (devInfos.Count == 0)
            {
                throw new IOException("No FTDI device available");
            }

            // Select the one from bus Id
            if (devInfos.Count < _settings.BusId)
            {
                throw new IOException($"Can't find a device to open SPI on index {_settings.BusId}");
            }

            // FT4222 propose depending on the mode multiple interfaces. In mode 0 Only the A is available for SPI
            // In mode 1 A, B and C are available
            // In mode 2 A, B, C and D are available
            // In mode 3 the only interface is available
            var devInfo = devInfos[_settings.BusId];
            if ((devInfo.Description == "FT4222 B" && devInfo.Type == FtDeviceType.Ft4222HMode0or2With2Interfaces) ||
                (devInfo.Description == "FT4222 D" && devInfo.Type == FtDeviceType.Ft4222HMode1or2With4Interfaces))
            {
                throw new IOException($"No SPI capable device on index {_settings.BusId}");
            }

            DeviceInformation = new(devInfo);

            // Open device
            var ftStatus = FtFunction.FT_OpenEx(DeviceInformation.LocId, FtOpenType.OpenByLocation, out _ftHandle);

            if (ftStatus != FtStatus.Ok)
            {
                throw new IOException($"Failed to open device {DeviceInformation.Description} with error: {ftStatus}");
            }

            // Set the clock but we need some math
            var (ft4222Clock, tfSpiDiv) = CalculateBestClockRate();

            ftStatus = FtFunction.FT4222_SetClock(_ftHandle, ft4222Clock);
            if (ftStatus != FtStatus.Ok)
            {
                throw new IOException($"Failed to set clock rate {ft4222Clock} on device: {DeviceInformation.Description}with error: {ftStatus}");
            }

            SpiClockPolarity pol = SpiClockPolarity.ClockIdleLow;
            if ((_settings.Mode == SpiMode.Mode2) || (_settings.Mode == SpiMode.Mode3))
            {
                pol = SpiClockPolarity.ClockIdelHigh;
            }

            SpiClockPhase pha = SpiClockPhase.ClockLeading;
            if ((_settings.Mode == SpiMode.Mode1) || (_settings.Mode == SpiMode.Mode3))
            {
                pha = SpiClockPhase.ClockTailing;
            }

            // Configure the SPI
            ftStatus = FtFunction.FT4222_SPIMaster_Init(_ftHandle, SpiOperatingMode.Single, tfSpiDiv, pol, pha,
                (byte)_settings.ChipSelectLine);
            if (ftStatus != FtStatus.Ok)
            {
                throw new IOException($"Failed setup SPI on device: {DeviceInformation.Description} with error: {ftStatus}");
            }
        }

        // Maximum is the System Clock / 1 = 80 MHz
        // Minimum is the System Clock / 512 = 24 / 256 = 93.75 KHz
        // Always take the below frequency to avoid over clocking
        private (FtClockRate Clk, SpiClock SpiClk) CalculateBestClockRate() => _settings.ClockFrequency switch
        {
            < 187500 => (FtClockRate.Clock24MHz, SpiClock.DivideBy256),
            < 234375 => (FtClockRate.Clock48MHz, SpiClock.DivideBy256),
            < 312500 => (FtClockRate.Clock60MHz, SpiClock.DivideBy256),
            < 375000 => (FtClockRate.Clock80MHz, SpiClock.DivideBy256),
            < 468750 => (FtClockRate.Clock48MHz, SpiClock.DivideBy128),
            < 625000 => (FtClockRate.Clock60MHz, SpiClock.DivideBy128),
            < 750000 => (FtClockRate.Clock80MHz, SpiClock.DivideBy128),
            < 937500 => (FtClockRate.Clock48MHz, SpiClock.DivideBy64),
            < 1250000 => (FtClockRate.Clock60MHz, SpiClock.DivideBy64),
            < 1500000 => (FtClockRate.Clock80MHz, SpiClock.DivideBy64),
            < 1875000 => (FtClockRate.Clock48MHz, SpiClock.DivideBy32),
            < 2500000 => (FtClockRate.Clock60MHz, SpiClock.DivideBy32),
            < 3000000 => (FtClockRate.Clock80MHz, SpiClock.DivideBy32),
            < 3750000 => (FtClockRate.Clock48MHz, SpiClock.DivideBy16),
            < 5000000 => (FtClockRate.Clock60MHz, SpiClock.DivideBy16),
            < 6000000 => (FtClockRate.Clock80MHz, SpiClock.DivideBy16),
            < 7500000 => (FtClockRate.Clock48MHz, SpiClock.DivideBy8),
            < 10000000 => (FtClockRate.Clock60MHz, SpiClock.DivideBy8),
            < 12000000 => (FtClockRate.Clock80MHz, SpiClock.DivideBy8),
            < 15000000 => (FtClockRate.Clock48MHz, SpiClock.DivideBy4),
            < 20000000 => (FtClockRate.Clock60MHz, SpiClock.DivideBy4),
            < 24000000 => (FtClockRate.Clock80MHz, SpiClock.DivideBy4),
            < 30000000 => (FtClockRate.Clock48MHz, SpiClock.DivideBy2),
            < 40000000 => (FtClockRate.Clock60MHz, SpiClock.DivideBy2),
            < 48000000 => (FtClockRate.Clock80MHz, SpiClock.DivideBy2),
            < 60000000 => (FtClockRate.Clock48MHz, SpiClock.DivideBy1),
            < 80000000 => (FtClockRate.Clock60MHz, SpiClock.DivideBy1),
            // Anything else will be 80 MHz
            _ => (FtClockRate.Clock80MHz, SpiClock.DivideBy1),
        };

        /// <inheritdoc/>
        public override void Read(Span<byte> buffer)
        {
            ushort readBytes;
            var ftStatus = FtFunction.FT4222_SPIMaster_SingleRead(_ftHandle, in MemoryMarshal.GetReference(buffer),
                (ushort)buffer.Length, out readBytes, true);
            if (ftStatus != FtStatus.Ok)
            {
                throw new IOException($"{nameof(Read)} failed to read, error: {ftStatus}");
            }
        }

        /// <inheritdoc/>
        public override byte ReadByte()
        {
            Span<byte> toRead = stackalloc byte[1];
            Read(toRead);
            return toRead[0];
        }

        /// <inheritdoc/>
        public override void TransferFullDuplex(ReadOnlySpan<byte> writeBuffer, Span<byte> readBuffer)
        {
            ushort readBytes;
            var ftStatus = FtFunction.FT4222_SPIMaster_SingleReadWrite(_ftHandle,
                in MemoryMarshal.GetReference(readBuffer), in MemoryMarshal.GetReference(writeBuffer),
                (ushort)writeBuffer.Length, out readBytes, true);
            if (ftStatus != FtStatus.Ok)
            {
                throw new IOException($"{nameof(TransferFullDuplex)} failed to do a full duplex transfer, error: {ftStatus}");
            }
        }

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ushort bytesWritten;
            var ftStatus = FtFunction.FT4222_SPIMaster_SingleWrite(_ftHandle, in MemoryMarshal.GetReference(buffer),
                (ushort)buffer.Length, out bytesWritten, true);
            if (ftStatus != FtStatus.Ok)
            {
                throw new IOException($"{nameof(Write)} failed to write, error: {ftStatus}");
            }
        }

        /// <inheritdoc/>
        public override void WriteByte(byte value)
        {
            Span<byte> toWrite = stackalloc byte[1]
            {
                value
            };
            Write(toWrite);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            _ftHandle.Dispose();
            base.Dispose(disposing);
        }
    }
}
