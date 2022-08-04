﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*
Handshake
ParityReplace
BreakState
DiscardNull
ReadTimeout
WriteTimeout

DtrEnable
RtsEnable

CDHolding
CtsHolding
DsrHolding

*/

using System.Text;

namespace System.Device.Ports.SerialPort
{
    public partial class SerialPort
    {
        /// <summary>
        /// Indicates that no time-out should occur.
        /// </summary>
        public const int InfiniteTimeout = -1;

        private const int MaxDataBitsNoParity = 9;
        private const int MinDataBits = 5;
        private const int DefaultBaudRate = 9600;
        private const Parity DefaultParity = Parity.None;
        private const int DefaultDataBits = 8;
        private const StopBits DefaultStopBits = StopBits.One;
        private const Handshake DefaultHandshake = Handshake.None;
        private const bool DefaultDtrEnable = false;
        private const bool DefaultRtsEnable = false;
        private const bool DefaultDiscardNull = false;
        private const byte DefaultParityReplace = (byte)'?';
        /*private const int DefaultBufferSize = 1024;*/
        private const int DefaultReadBufferSize = 4096;
        private const int DefaultWriteBufferSize = 2048;

        private const int DefaultReceivedBytesThreshold = 1;
        private const int DefaultReadTimeout = InfiniteTimeout;
        private const int DefaultWriteTimeout = InfiniteTimeout;

        private bool _isOpen;
        private int _baudRate;
        private Parity _parity;
        private int _dataBits;
        private StopBits _stopBits;
        private bool _breakState;
        private bool _discardNull = DefaultDiscardNull;
        private bool _dtrEnable = DefaultDtrEnable;
        private Encoding _encoding = Encoding.ASCII;
        private Handshake _handshake = DefaultHandshake;
        private string _newLine = Environment.NewLine;
        private byte _parityReplace = DefaultParityReplace;

        /// <summary>
        /// The name of the serial port whose default value is platform dependent
        /// and set to a proper default name in the derived platform-specific classes
        /// </summary>
        protected string _portName = String.Empty;

        private int _readBufferSize = DefaultReadBufferSize;
        private int _readTimeout = DefaultReadTimeout;
        private int _receivedBytesThreshold = DefaultReceivedBytesThreshold;
        private bool _rtsEnable = DefaultRtsEnable;
        private int _writeBufferSize = DefaultWriteBufferSize;
        private int _writeTimeout = DefaultWriteTimeout;

        /// <summary>
        /// The baud rate
        /// </summary>
        public int BaudRate
        {
            get => _baudRate;
            set
            {
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(BaudRate), Strings.ArgumentOutOfRange_NeedPosNum);
                }

                if (value == _baudRate)
                {
                    return;
                }

                _baudRate = value;
                SetBaudRate(_baudRate);
            }
        }

        /// <summary>
        /// Set the baud rate
        /// </summary>
        /// <param name="baudRate">The baud rate to set</param>
        protected internal abstract void SetBaudRate(int baudRate);

        /// <summary>
        /// The parity
        /// </summary>
        public Parity Parity
        {
            get => _parity;
            set
            {
                if (value < Parity.None || value > Parity.Space)
                {
                    throw new ArgumentOutOfRangeException(nameof(Parity), Strings.ArgumentOutOfRange_Enum);
                }

                if (value == _parity)
                {
                    return;
                }

                _parity = value;
                SetParity(_parity);
            }
        }

        /// <summary>
        /// Set the communication parity
        /// </summary>
        /// <param name="parity">The parity value to set</param>
        protected internal abstract void SetParity(Parity parity);

        /// <summary>
        /// The data bits
        /// </summary>
        public int DataBits
        {
            get => _dataBits;
            set
            {
                // 9 data bit is only supported by toggling the parity bit
                if (_dataBits < MinDataBits || _dataBits > MaxDataBitsNoParity ||
                    (_dataBits == MaxDataBitsNoParity && Parity != Parity.None))
                {
                    throw new ArgumentOutOfRangeException(nameof(DataBits), Strings.InvalidDataBits);
                }

                if (value == _dataBits)
                {
                    return;
                }

                _dataBits = value;
                SetDataBits(_dataBits);
            }
        }

        /// <summary>
        /// Set the communication data bits
        /// </summary>
        /// <param name="dataBits">The data bits value to set</param>
        protected internal abstract void SetDataBits(int dataBits);

        /// <summary>
        /// The stop bits
        /// </summary>
        public StopBits StopBits
        {
            get => _stopBits;
            set
            {
                if (value < StopBits.One || value > StopBits.OnePointFive)
                {
                    throw new ArgumentOutOfRangeException(nameof(StopBits), Strings.ArgumentOutOfRange_Enum);
                }

                if (value == _stopBits)
                {
                    return;
                }

                _stopBits = value;
                SetStopBits(_stopBits);
            }
        }

        /// <summary>
        /// Set the communication stop bits
        /// </summary>
        /// <param name="stopBits">The stop bits value to set</param>
        protected internal abstract void SetStopBits(StopBits stopBits);

        /// <summary>
        /// Gets or sets the break signal state.
        /// </summary>
        public bool BreakState
        {
            get => _breakState;
            set
            {
                /*
                if (value == _breakState)
                {
                    return;
                }
                */

                _breakState = value;
                SetBreakState(_breakState);
            }
        }

        /// <summary>
        /// Sets the break signal state.
        /// </summary>
        /// <param name="breakState">true if the port is in a break state; otherwise, false.</param>
        protected internal abstract void SetBreakState(bool breakState);

        /// <summary>
        /// Gets the number of bytes of data in the receive buffer.
        /// </summary>
        public int BytesToRead
        {
            get
            {
                if (!_isOpen)
                {
                    throw new InvalidOperationException(Strings.Port_not_open);
                }

                return GetBytesToRead();
            }
        }

        /// <summary>
        /// Gets the number of bytes of data in the receive buffer.
        /// </summary>
        protected internal abstract int GetBytesToRead();

        /// <summary>
        /// Gets the number of bytes of data in the send buffer.
        /// </summary>
        public int BytesToWrite
        {
            get
            {
                if (!_isOpen)
                {
                    throw new InvalidOperationException(Strings.Port_not_open);
                }

                return GetBytesToWrite();
            }
        }

        /// <summary>
        /// Gets the number of bytes of data in the send buffer.
        /// </summary>
        protected internal abstract int GetBytesToWrite();

        /// <summary>
        /// Gets the state of the Carrier Detect line for the port.
        /// </summary>
        public int CDHolding
        {
            get
            {
                if (!_isOpen)
                {
                    throw new InvalidOperationException(Strings.Port_not_open);
                }

                return GetCDHolding();
            }
        }

        /// <summary>
        /// Gets the state of the Carrier Detect line for the port.
        /// </summary>
        protected internal abstract int GetCDHolding();

        /// <summary>
        /// Gets the state of the Clear-to-Send line.
        /// </summary>
        public int CtsHolding
        {
            get
            {
                if (!_isOpen)
                {
                    throw new InvalidOperationException(Strings.Port_not_open);
                }

                return GetCtsHolding();
            }
        }

        /// <summary>
        /// Gets the state of the Clear-to-Send line.
        /// </summary>
        protected internal abstract int GetCtsHolding();

        /// <summary>
        /// Gets or sets a value indicating whether null bytes are ignored when
        /// transmitted between the port and the receive buffer.
        /// </summary>
        public bool DiscardNull
        {
            get => _discardNull;
            set
            {
                /*
                if (value == _discardNull)
                {
                    return;
                }
                */

                _discardNull = value;
                SetDiscardNull(_discardNull);
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether null bytes are ignored when
        /// transmitted between the port and the receive buffer.
        /// </summary>
        /// <param name="value">true if null bytes are ignored; otherwise false. The default is false.</param>
        protected internal abstract void SetDiscardNull(bool value);

        /// <summary>
        /// Gets the state of the Data Set Ready (DSR) signal.
        /// </summary>
        public int DsrHolding
        {
            get
            {
                if (!_isOpen)
                {
                    throw new InvalidOperationException(Strings.Port_not_open);
                }

                return GetDsrHolding();
            }
        }

        /// <summary>
        /// Gets the state of the Data Set Ready (DSR) signal.
        /// </summary>
        protected internal abstract int GetDsrHolding();

        /// <summary>
        /// Gets or sets a value that enables the Data Terminal Ready (DTR) signal during serial communication.
        /// </summary>
        public bool DtrEnable
        {
            get => _dtrEnable;
            set
            {
                /*
                if (value == _dtrEnable)
                {
                    return;
                }
                */

                _dtrEnable = value;
                SetDtrEnable(_dtrEnable);
            }
        }

        /// <summary>
        /// Gets or sets a value that enables the Data Terminal Ready (DTR) signal during serial communication.
        /// </summary>
        /// <param name="value">true to enable Data Terminal Ready (DTR); otherwise, false. The default is false.</param>
        protected internal abstract void SetDtrEnable(bool value);

        /// <summary>
        /// Gets or sets the byte encoding for pre- and post-transmission conversion of text.
        /// </summary>
        public Encoding Encoding
        {
            get => _encoding;
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(Encoding));
                }

                if (value == _encoding)
                {
                    return;
                }

                /*
                // Limit the encodings we support to some known ones.  The code pages < 50000 represent all of the single-byte
                // and double-byte code pages.  Code page 54936 is GB18030.
                if (!(value is ASCIIEncoding || value is UTF8Encoding || value is UnicodeEncoding || value is UTF32Encoding ||
                      value.CodePage < 50000 || value.CodePage == 54936))
                {
                    throw new ArgumentException(SR.Format(SR.NotSupportedEncoding, value.WebName), nameof(Encoding));
                }

                _encoding = value;
                _decoder = _encoding.GetDecoder();

                // This is somewhat of an approximate guesstimate to get the max char[] size needed to encode a single character
                _maxByteCountForSingleChar = _encoding.GetMaxByteCount(1);
                _singleCharBuffer = null;
                 */

                _encoding = value;
            }
        }

        /// <summary>
        /// Gets or sets the handshaking protocol for serial port transmission of data using a value from Handshake.
        /// </summary>
        public Handshake Handshake
        {
            get => _handshake;
            set
            {
                if (value < Handshake.None || value > Handshake.RequestToSendXOnXOff)
                {
                    throw new ArgumentOutOfRangeException(nameof(Handshake), Strings.ArgumentOutOfRange_Enum);
                }

                if (value == _handshake)
                {
                    return;
                }

                _handshake = value;
                SetHandshake(_handshake);
            }
        }

        /// <summary>
        /// Gets or sets the handshaking protocol for serial port transmission of data using a value from Handshake.
        /// </summary>
        /// <param name="handshake">One of the Handshake values. The default is None.</param>
        protected internal abstract void SetHandshake(Handshake handshake);

        /// <summary>
        /// Gets a value indicating the open or closed status of the SerialPort object.
        /// </summary>
        public bool IsOpen => _isOpen;

        /// <summary>
        /// Gets or sets the value used to interpret the end of a call to the ReadLine() and WriteLine(String) methods.
        /// </summary>
        public string NewLine
        {
            get => _newLine;
            set
            {
                if (value == _newLine)
                {
                    return;
                }

                if (value == null)
                {
                    throw new ArgumentNullException(nameof(NewLine));
                }

                if (value.Length == 0)
                {
                    throw new ArgumentException(string.Format(Strings.EmptyString, nameof(NewLine)));
                }

                _newLine = value;
            }
        }

        /// <summary>
        /// Gets or sets the byte that replaces invalid bytes in a data stream when a parity error occurs.
        /// </summary>
        public byte ParityReplace
        {
            get => _parityReplace;
            set
            {
                if (value == _parityReplace)
                {
                    return;
                }

                _parityReplace = value;
                SetParityReplace(_parityReplace);
            }
        }

        /// <summary>
        /// Gets or sets the byte that replaces invalid bytes in a data stream when a parity error occurs.
        /// </summary>
        /// <param name="parityReplace">A byte that replaces invalid bytes.</param>
        /// <returns></returns>
        protected internal abstract byte SetParityReplace(byte parityReplace);

        /// <summary>
        /// Gets or sets the value used to interpret the end of a call to the ReadLine() and WriteLine(String) methods.
        /// </summary>
        public string PortName
        {
            get => _portName;
            set
            {
                if (value == _portName)
                {
                    return;
                }

                if (_portName == null)
                {
                    throw new ArgumentNullException(nameof(PortName));
                }

                if (value.Length == 0)
                {
                    throw new ArgumentException(string.Format(Strings.EmptyString, nameof(PortName)));
                }

                if (IsOpen)
                {
                    throw new InvalidOperationException(string.Format(Strings.Cant_be_set_when_open, nameof(PortName)));
                }

                _portName = value;
            }
        }

        /// <summary>
        /// Gets or sets the size of the SerialPort input buffer.
        /// </summary>
        public int ReadBufferSize
        {
            get
            {
                return _readBufferSize;
            }
            set
            {
                if (value == _readBufferSize)
                {
                    return;
                }

                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(ReadBufferSize));
                }

                if (IsOpen)
                {
                    throw new InvalidOperationException(string.Format(Strings.Cant_be_set_when_open, nameof(ReadBufferSize)));
                }

                _readBufferSize = value;
            }
        }

        /// <summary>
        /// Gets or sets the number of milliseconds before a time-out occurs when a read operation does not finish.
        /// </summary>
        public int ReadTimeout
        {
            get
            {
                return _readTimeout;
            }
            set
            {
                if (value == _readTimeout)
                {
                    return;
                }

                if (value < 0 && value != InfiniteTimeout)
                {
                    throw new ArgumentOutOfRangeException(nameof(ReadTimeout), Strings.ArgumentOutOfRange_Timeout);
                }

                _readTimeout = value;
                SetReadTimeout(_readTimeout);
            }
        }

        /// <summary>
        /// Gets or sets the number of milliseconds before a time-out occurs when a read operation does not finish.
        /// </summary>
        /// <param name="timeout">The number of milliseconds before a time-out occurs when a read operation does not finish.</param>
        protected internal abstract void SetReadTimeout(int timeout);

        /// <summary>
        /// Gets or sets the number of bytes in the internal input buffer before a DataReceived event occurs.
        /// </summary>
        public int ReceivedBytesThreshold
        {
            get
            {
                return _receivedBytesThreshold;
            }
            set
            {
                if (value == _receivedBytesThreshold)
                {
                    return;
                }

                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(ReceivedBytesThreshold), Strings.ArgumentOutOfRange_NeedPosNum);
                }

                _receivedBytesThreshold = value;

                if (IsOpen)
                {
                    OnReceivedBytesThresholdChanged();
                }
            }
        }

        private void OnReceivedBytesThresholdChanged()
        {
            // fake the call to our event handler in case the threshold has been set lower
            // than how many bytes we currently have.
            SerialDataReceivedEventArgs args = new SerialDataReceivedEventArgs(SerialData.Chars);
            /*CatchReceivedEvents(this, args);*/
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets or sets a value indicating whether the Request to Send (RTS) signal is enabled during serial communication.
        /// </summary>
        public bool RtsEnable
        {
            get => _rtsEnable;
            set
            {
                if (value == _rtsEnable)
                {
                    return;
                }

                _rtsEnable = value;
                SetRtsEnable(_rtsEnable);
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the Request to Send (RTS) signal is enabled during serial communication.
        /// </summary>
        /// <param name="rtsEnable">true to enable Request to Transmit (RTS); otherwise, false. The default is false.</param>
        protected internal abstract void SetRtsEnable(bool rtsEnable);

        /// <summary>
        /// Gets or sets the size of the serial port output buffer.
        /// </summary>
        public int WriteBufferSize
        {
            get
            {
                return _writeBufferSize;
            }
            set
            {
                if (value == _writeBufferSize)
                {
                    return;
                }

                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(WriteBufferSize));
                }

                if (IsOpen)
                {
                    throw new InvalidOperationException(string.Format(Strings.Cant_be_set_when_open, nameof(WriteBufferSize)));
                }

                _writeBufferSize = value;
                SetWriteBufferSize(_writeBufferSize);
            }
        }

        /// <summary>
        /// Gets or sets the size of the serial port output buffer.
        /// </summary>
        /// <param name="writeBufferSize">The size of the output buffer. The default is 2048.</param>
        protected internal abstract void SetWriteBufferSize(int writeBufferSize);

        /// <summary>
        /// Gets or sets the number of milliseconds before a time-out occurs when a write operation does not finish.
        /// </summary>
        public int WriteTimeout
        {
            get => _writeTimeout;
            set
            {
                if (value == _writeTimeout)
                {
                    return;
                }

                if (value <= 0 && value != InfiniteTimeout)
                {
                    throw new ArgumentOutOfRangeException(nameof(WriteTimeout), Strings.ArgumentOutOfRange_WriteTimeout);
                }

                _writeTimeout = value;
                SetWriteTimeout(_writeTimeout);
            }
        }

        /// <summary>
        /// Gets or sets the number of milliseconds before a time-out occurs when a write operation does not finish.
        /// </summary>
        /// <param name="writeTimeout">The number of milliseconds before a time-out occurs. The default is InfiniteTimeout.</param>
        protected internal abstract void SetWriteTimeout(int writeTimeout);
    }
}