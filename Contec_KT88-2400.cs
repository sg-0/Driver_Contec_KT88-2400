/* 
 * Created on Jan 20, 2025
 * @author: Sandro Gantenbein
 * if you have questions please write me an email: info@brainli.ch
 */

using System;
using System.IO.Ports;
using System.Threading;

namespace ContecKT88_2400_Driver
{
    public static class ChannelInfo
    {
        // 26 channel labels in the same order your driver returns data
        public static readonly string[] Labels = new string[]
        {
            "Fp1", "Fp2", "F3", "F4", "C3", "C4",
            "P3", "P4", "O1", "O2", "F7", "F8",
            "T3", "T4", "T5", "T6", "Fz", "Pz",
            "Cz", "Pg1", "Pg2", "EOGR", "EOOGL", "EMG", "BR", "ECG"
        };
    }
    /// <summary>
    /// Enumeration for hardware filter commands.
    /// </summary>
    public enum HardwareFilter
    {
        Enable = 0x03, // 0.5 - 35 Hz filter
        Disable = 0x04
    }

    /// <summary>
    /// Enumeration for impedance measurement commands.
    /// </summary>
    public enum Impedance
    {
        Start = 0x05,
        Stop = 0x06
    }

    /// <summary>
    /// Enumeration for references (AA,A1,A2,AVG,Cz,BN)
    /// </summary>
    public enum ReferenceType
    {
        AA = 0x01,  // Left/Right earlobes
        A1 = 0x02,  // A1 reference
        A2 = 0x03,  // A2 reference
        AVG = 0x04,  // Average
        Cz = 0x05,  //Cz reference
        BN = 0x06   // Balanced noncephalic reference
    }

    /// <summary>
    /// Encapsulates KT-88 montage options (default is 01 with AA-reference montage). 
    /// Read the readme for further information
    /// </summary>
    public enum Montage
    {
        Montage1 = 0x01,
        Montage2 = 0x02,
        Montage3 = 0x03,
        Montage4 = 0x04,
        Montage5 = 0x05,
        Montage6 = 0x06,
        Montage7 = 0x07,
        Montage8 = 0x08,
        Montage9 = 0x09
    }

    /// <summary>
    /// Driver class to control and read data from the KT-88 2400 EEG device.
    /// </summary>
    public class ContecKT88_2400
    {
        // Serial port for device communication
        private SerialPort _serialPort;

        // Thread to continuously read EEG data
        private Thread _dataThread;
        private bool _keepReading = false;

        /// <summary>
        /// Event raised when a new EEG sample is available.
        /// The float[] array will contain 26 channels of data.
        /// </summary>
        public event Action<float[]> OnDataReceived;

        // Order matters: index 0 matches channels[0], etc.
        public static readonly string[] Labels = new string[]
        {
        "Fp1", "Fp2", "F3", "F4", "C3", "C4",
        "P3", "P4", "O1", "O2", "F7", "F8",
        "T3", "T4", "T5", "T6", "Fz", "Pz",
        "Cz", "Pg1", "Pg2", "EOGR", "EOOGL", "EMG", "BR", "ECG"
        };

        /// <summary>
        /// Opens the serial port to the KT-88 device.
        /// </summary>
        /// <param name="portName">COM port name (e.g. "COM3").</param>
        /// <returns>True if opened successfully, False otherwise.</returns>
        public bool Open(string portName)
        {
            try
            {
                _serialPort = new SerialPort(portName, 921600, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };
                _serialPort.Open();
                return _serialPort.IsOpen;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to open port {portName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Closes the serial port and stops any ongoing acquisition.
        /// </summary>
        public void Close()
        {
            StopAcquisition(); // Ensure acquisition thread is stopped
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
                _serialPort.Dispose();
            }
        }

        #region --- High-Level Commands (Send/Receive) ---

        /// <summary>
        /// Sends a two-byte command packet. For example, [0x90, 0x01] to start acquisition.
        /// </summary>
        /// <param name="firstByte">The first command byte.</param>
        /// <param name="secondByte">The second command byte.</param>
        private void SendCommand(byte firstByte, byte secondByte)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

            byte[] packet = { firstByte, secondByte };
            try
            {
                _serialPort.Write(packet, 0, packet.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] SendCommand failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts EEG data acquisition (sends 90 01).
        /// </summary>
        public void StartAcquisition()
        {
            // Start data reading thread
            if (!_keepReading)
            {
                _keepReading = true;
                _dataThread = new Thread(ReadDataLoop)
                {
                    IsBackground = true
                };
                _dataThread.Start();
            }

            SendCommand(0x90, 0x01);
        }

        /// <summary>
        /// Stops EEG data acquisition (sends 90 02).
        /// </summary>
        public void StopAcquisition()
        {
            SendCommand(0x90, 0x02);

            // Stop data thread
            if (_keepReading)
            {
                _keepReading = false;
                _dataThread?.Join(500); // wait up to 500ms for thread to exit
            }
        }

        /// <summary>
        /// Enables or disables the hardware filter.
        /// see:
        /// https://patents.google.com/patent/CN103505200A/en
        /// </summary>
        public void SetHardwareFilter(HardwareFilter filter)
        {
            SendCommand(0x90, (byte)filter);
        }

        /// <summary>
        /// Starts or stops impedance measurement.
        /// The LEDs on the EEG start shine green if impedance is OK
        /// make sure you stop impedance measurement before data aquisition starts
        /// </summary>
        public void SetImpedanceMeasurement(Impedance impedance)
        {
            SendCommand(0x90, (byte)impedance);
        }

        /// <summary>
        /// Sets the physical reference electrode
        /// </summary>
        public void SetReference(ReferenceType refType)
        {
            SendCommand(0x91, (byte)refType);
        }

        /// <summary>
        /// Sets the montage 
        /// read readme for further infos
        /// </summary>
        public void SetMontage(Montage montage)
        {
            SendCommand(0x92, (byte)montage);
        }

        /// <summary>
        /// Sends the "default configuration" sequence from Contec s EEG-Studio
        /// This command takes about 2.5 seconds
        /// </summary>
        public void SendDefaultConfiguration()
        {
            // 1) Stop acquisition
            SendCommand(0x90, 0x02);
            Thread.Sleep(300);

            // 2) default commands 80 00 and 81 00 (buffer)
            SendCommand(0x80, 0x00);
            Thread.Sleep(300);
            SendCommand(0x81, 0x00);
            Thread.Sleep(300);

            // 3) Set AA reference
            SendCommand(0x91, 0x01);
            Thread.Sleep(300);

            // 4) additional command
            SendCommand(0x90, 0x09);
            Thread.Sleep(300);

            // 5) Enable hardware filter
            SendCommand(0x90, 0x03);
            Thread.Sleep(300);

            // 6) Disable impedance measurement
            SendCommand(0x90, 0x06);
            Thread.Sleep(300);

            // Flush any old data
            if (_serialPort?.IsOpen == true)
            {
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
            }
        }

        #endregion

        #region --- Data Reading / Parsing ---

        /// <summary>
        /// Continuously reads the data stream while acquisition is active.
        /// Each data frame is 46 bytes (including two start bytes e0 01),
        /// though the device may output 0xA0 or 0xE0 for synchronization.
        /// 
        /// On success, calls OnDataReceived with a float[26].
        /// </summary>
        private void ReadDataLoop()
        {
            try
            {
                // Clear buffers before starting
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
            }
            catch { /* ignore if not open yet */ }

            while (_keepReading)
            {
                try
                {
                    // First, read until we find 0xE0 or 0xA0 indicating start of packet
                    // In the python code: read_until(0xA0).
                    // proposal: simple loop to find the marker.
                    int firstByte = _serialPort.ReadByte();
                    if (firstByte < 0) continue; // no data

                    if (firstByte == 0xA0 || firstByte == 0xE0)
                    {
                        // We expect 45 more bytes = 46 total per block (or 46 if 0xe0 is included)
                        byte[] chunk = new byte[45];
                        int bytesRead = _serialPort.Read(chunk, 0, 45);
                        if (bytesRead < 45) continue; // incomplete packet

                        // Parse the 46-byte packet:
                        // The first byte is already in 'firstByte'; the rest are in 'chunk'.
                        // Combine them for convenience.
                        byte[] fullPacket = new byte[46];
                        fullPacket[0] = (byte)firstByte;
                        Array.Copy(chunk, 0, fullPacket, 1, 45);

                        // Attempt to parse EEG data
                        float[] channels = ParseEegData(fullPacket);
                        OnDataReceived?.Invoke(channels);
                    }
                }
                catch (TimeoutException)
                {
                    // Normal if there's a short gap in data
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Data reading failed: {ex.Message}");
                    Thread.Sleep(50);
                }
            }
        }

        /// <summary>
        /// Parse 46 bytes of data into 26 channels based on the bit manipulation logic: https://openvibe.inria.fr/forum/viewtopic.php?t=10239
        /// </summary>
        private float[] ParseEegData(byte[] packet)
        {

            // 2 start bytes (e0 01) or (a0 ...). 
            // 46 bytes per data block.

            const int numChannels = 26;
            float[] channels = new float[numChannels];

            if (packet.Length < 46)
                return channels;

            byte[] chunk = new byte[45];
            Array.Copy(packet, 1, chunk, 0, 45); // skip the first marker byte


            int[] channelRaw = new int[numChannels];
            for (int i = 0; i < numChannels; i++)
            {
                channelRaw[i] = 0;
            }

            int cIndex = 0;
            for (int bt = 6; bt < 45; bt += 3)
            {
                channelRaw[cIndex] =
                    ((chunk[bt] & 0b01110000) << 4) |
                     (chunk[bt + 1] & 0b01111111);

                if (bt + 2 < 45 && cIndex + 1 < numChannels)
                {
                    channelRaw[cIndex + 1] =
                        ((chunk[bt] & 0b00001111) << 8) |
                         (chunk[bt + 2] & 0b01111111);
                }
                cIndex += 2;
                if (cIndex >= numChannels) break;
            }

            channelRaw[0] |= ((chunk[0] & 0b00000001) << 11) | ((chunk[0] & 0b00000010) << 6);


            // Convert the 12-bit raw data to float (subtract 2048, scale by 1/10)
            for (int i = 0; i < numChannels; i++)
            {
                channels[i] = (channelRaw[i] - 2048) / 10.0f;
            }

            return channels;
        }

        #endregion
    }
}
