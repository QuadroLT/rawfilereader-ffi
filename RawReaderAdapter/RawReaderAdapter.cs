using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using ThermoFisher.CommonCore.Data;
using ThermoFisher.CommonCore.Data.Business;
using ThermoFisher.CommonCore.Data.FilterEnums;
using ThermoFisher.CommonCore.Data.Interfaces;
using ThermoFisher.CommonCore.RawFileReader;
using ThermoFisher.CommonCore.BackgroundSubtraction;


namespace RawReaderAdapter
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RawSpectrumData
    {
        public double* masses;
        public double* intensities;
        public int count;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawScanStats
    {
        public int scan_number;
        public int ms_level;
        public int polarity;
        public int is_centroid;
        public int packet_count;
        public double retention_time;
        public double tic;
        public double base_peak_mass;
        public double base_peak_intensity;
        public double low_mass;
        public double high_mass;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RawChromatogramData
    {
        public double* times;
        public double* intensities;
        public int count;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawPrecursorInfo
    {
        public int master_scan;
        public double precursor_mass;
        public double isolation_width;
        public int charge_state;
        public double monoisotopic_mass;
        public double collision_energy;
    }

    internal sealed class SharedFile
    {
        public IRawFileThreadManager Manager { get; }
        public string NormalizedPath { get; }
        public int References;

        public SharedFile(IRawFileThreadManager manager, string normalizedPath)
        {
            Manager = manager;
            NormalizedPath = normalizedPath;
            References = 1;
        }
    }

    internal sealed class OpenFileSession
    {
        public IRawDataPlus RawFile { get; }
        public SharedFile Shared { get; }
        public readonly object Lock = new();

        public OpenFileSession(IRawDataPlus rawFile, SharedFile shared)
        {
            RawFile = rawFile;
            Shared = shared;
        }
    }

    public static unsafe class RawReaderAdapter
    {
        private static readonly object _fileLock = new();
        private static readonly System.Collections.Generic.Dictionary<string, SharedFile> _sharedFiles = new(StringComparer.OrdinalIgnoreCase);

        public const int RAW_SUCCESS                  = 0;

        // File errors (100 - 199)
        public const int RAW_ERROR_FILE_NAME_EMPTY    = 101;
        public const int RAW_ERROR_FILE_NOT_FOUND     = 102;
        public const int RAW_ERROR_FILE_OPEN_FAILED   = 103;
        public const int RAW_ERROR_FILE_CORRUPT       = 104;
        public const int RAW_ERROR_FILE_UNDEFINED     = 199;

        // Handle & Pointer errors (200 - 299)
        public const int RAW_ERROR_NULL_POINTER       = 201;
        public const int RAW_ERROR_INVALID_HANDLE     = 202;

        // Scan & Data errors (300 - 399)
        public const int RAW_ERROR_SCAN_OUT_OF_RANGE  = 301;
        public const int RAW_ERROR_NO_DATA            = 302;
        public const int RAW_ERROR_NO_CENTROID_STREAM = 303;
        public const int RAW_ERROR_NO_PRECURSOR_INFO  = 304;
        public const int RAW_ERROR_NO_INSTRUMENT      = 305;

        // General errors (400 - 499)
        public const int RAW_ERROR_OUT_OF_MEMORY      = 401;
        public const int RAW_ERROR_BUFFER_TOO_SMALL   = 402;
        public const int RAW_ERROR_UNEXPECTED         = 499;

        static RawReaderAdapter()
        {
            // Preserve MetaFilterType array and enum values for NativeAOT
            _ = new ThermoFisher.CommonCore.Data.Interfaces.MetaFilterType[0];
            _ = Enum.GetValues<ThermoFisher.CommonCore.Data.Interfaces.MetaFilterType>();
        }

        #region Helpers

        private static bool TryGetRawFile(IntPtr fileHandle, out IRawDataPlus? rawFile, out object? sessionLock, out int errorCode)
        {
            rawFile = null;
            sessionLock = null;
            if (fileHandle == IntPtr.Zero)
            {
                errorCode = RAW_ERROR_INVALID_HANDLE;
                return false;
            }

            try
            {
                GCHandle handle = GCHandle.FromIntPtr(fileHandle);
                if (handle.IsAllocated)
                {
                    if (handle.Target is OpenFileSession session)
                    {
                        var rf = session.RawFile;
                        if (rf != null && rf.IsOpen)
                        {
                            rawFile = rf;
                            sessionLock = session.Lock;
                            errorCode = RAW_SUCCESS;
                            return true;
                        }
                    }
                    else if (handle.Target is IRawDataPlus rf && rf.IsOpen)
                    {
                        rawFile = rf;
                        sessionLock = rf;
                        errorCode = RAW_SUCCESS;
                        return true;
                    }
                }
                errorCode = RAW_ERROR_INVALID_HANDLE;
                return false;
            }
            catch
            {
                errorCode = RAW_ERROR_INVALID_HANDLE;
                return false;
            }
        }

        private static bool TryGetRawFile(IntPtr fileHandle, out IRawDataPlus? rawFile, out int errorCode)
        {
            return TryGetRawFile(fileHandle, out rawFile, out _, out errorCode);
        }


        private static byte* AllocUtf8String(string? str)
        {
            if (str == null) return null;
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            byte* buffer = (byte*)NativeMemory.Alloc((nuint)bytes.Length + 1);
            fixed (byte* pBytes = bytes)
            {
                Buffer.MemoryCopy(pBytes, buffer, bytes.Length + 1, bytes.Length);
            }
            buffer[bytes.Length] = 0; // null-terminated
            return buffer;
        }

        #endregion

        #region Core Managed Implementations

        private static int OpenFileCore(byte* filePathPtr, IntPtr* outHandle)
        {
            if (outHandle == null) return RAW_ERROR_NULL_POINTER;
            *outHandle = IntPtr.Zero;

            if (filePathPtr == null) return RAW_ERROR_NULL_POINTER;
            string? filePath = Marshal.PtrToStringUTF8((IntPtr)filePathPtr);
            if (string.IsNullOrEmpty(filePath)) return RAW_ERROR_FILE_NAME_EMPTY;

            if (!File.Exists(filePath)) return RAW_ERROR_FILE_NOT_FOUND;

            try
            {
                string fullPath = Path.GetFullPath(filePath);
                lock (_fileLock)
                {
                    SharedFile shared;
                    if (_sharedFiles.TryGetValue(fullPath, out var existing))
                    {
                        existing.References++;
                        shared = existing;
                    }
                    else
                    {
                        IRawFileThreadManager mgr = RawFileReaderAdapter.ThreadedFileFactory(filePath);
                        if (mgr == null) return RAW_ERROR_FILE_OPEN_FAILED;
                        shared = new SharedFile(mgr, fullPath);
                        _sharedFiles[fullPath] = shared;
                    }

                    IRawDataPlus? rawFile = null;
                    try
                    {
                        rawFile = shared.Manager.CreateThreadAccessor();
                        if (rawFile == null || !rawFile.IsOpen || rawFile.IsError)
                        {
                            rawFile?.Dispose();
                            ReleaseSharedLocked(shared);
                            return RAW_ERROR_FILE_OPEN_FAILED;
                        }

                        try
                        {
                            rawFile.SelectInstrument(Device.MS, 1);
                        }
                        catch
                        {
                            // Fall back if instrument selection cannot be performed immediately
                        }

                        var session = new OpenFileSession(rawFile, shared);
                        GCHandle handle = GCHandle.Alloc(session, GCHandleType.Normal);
                        *outHandle = GCHandle.ToIntPtr(handle);
                        return RAW_SUCCESS;
                    }
                    catch
                    {
                        rawFile?.Dispose();
                        ReleaseSharedLocked(shared);
                        throw;
                    }
                }
            }
            catch (FileNotFoundException)
            {
                return RAW_ERROR_FILE_NOT_FOUND;
            }
            catch
            {
                return RAW_ERROR_FILE_UNDEFINED;
            }
        }

        private static void ReleaseSharedLocked(SharedFile shared)
        {
            shared.References--;
            // Note: We intentionally keep the SharedFile in _sharedFiles and do not dispose
            // the underlying manager here, because Thermo Fisher CommonCore maintains internal
            // static caches by file path. Disposing the manager causes subsequent opens of the same
            // file to receive an invalidated/poisoned stream from Thermo's internal cache.
        }


        private static void CloseFileCore(IntPtr fileHandle)
        {
            if (fileHandle == IntPtr.Zero) return;
            try
            {
                GCHandle handle = GCHandle.FromIntPtr(fileHandle);
                if (handle.IsAllocated)
                {
                    if (handle.Target is OpenFileSession session)
                    {
                        lock (_fileLock)
                        {
                            ReleaseSharedLocked(session.Shared);
                        }
                    }
                    else if (handle.Target is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                    handle.Free();
                }
            }
            catch
            {
                // Silence cleanup errors
            }
        }




        private static int GetLastScanCore(IntPtr fileHandle, int* outLastScan)
        {
            if (outLastScan == null) return RAW_ERROR_NULL_POINTER;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                lock (sessionLock!)
                {
                    *outLastScan = rawFile!.RunHeaderEx.LastSpectrum;
                }
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }


        private static int GetSegmentedScanCore(IntPtr fileHandle, int scanNumber, RawSpectrumData* outData)
        {
            if (outData == null) return RAW_ERROR_NULL_POINTER;
            outData->masses = null;
            outData->intensities = null;
            outData->count = 0;

            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                lock (sessionLock!)
                {
                    SegmentedScan? scan = rawFile!.GetSegmentedScanFromScanNumber(scanNumber);
                    if (scan == null || scan.Positions == null || scan.Intensities == null)
                    {
                        return RAW_ERROR_NO_DATA;
                    }

                    int count = scan.Positions.Length;
                    if (count == 0)
                    {
                        return RAW_SUCCESS;
                    }

                    double* pMasses = (double*)NativeMemory.Alloc((nuint)count, (nuint)sizeof(double));
                    double* pIntensities = (double*)NativeMemory.Alloc((nuint)count, (nuint)sizeof(double));

                    fixed (double* srcM = scan.Positions, srcI = scan.Intensities)
                    {
                        Buffer.MemoryCopy(srcM, pMasses, (long)count * sizeof(double), (long)count * sizeof(double));
                        Buffer.MemoryCopy(srcI, pIntensities, (long)count * sizeof(double), (long)count * sizeof(double));
                    }

                    outData->masses = pMasses;
                    outData->intensities = pIntensities;
                    outData->count = count;
                    return RAW_SUCCESS;
                }
            }
            catch
            {
                return RAW_ERROR_SCAN_OUT_OF_RANGE;
            }
        }

        private static int GetCentroidStreamCore(IntPtr fileHandle, int scanNumber, RawSpectrumData* outData)
        {
            if (outData == null) return RAW_ERROR_NULL_POINTER;
            outData->masses = null;
            outData->intensities = null;
            outData->count = 0;

            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                lock (sessionLock!)
                {
                    CentroidStream? centroidStream = rawFile!.GetCentroidStream(scanNumber, false);
                    if (centroidStream == null || centroidStream.Masses == null || centroidStream.Intensities == null)
                    {
                        return RAW_ERROR_NO_CENTROID_STREAM;
                    }

                    int count = centroidStream.Masses.Length;
                    if (count == 0)
                    {
                        return RAW_SUCCESS;
                    }

                    double* pMasses = (double*)NativeMemory.Alloc((nuint)count, (nuint)sizeof(double));
                    double* pIntensities = (double*)NativeMemory.Alloc((nuint)count, (nuint)sizeof(double));

                    fixed (double* srcM = centroidStream.Masses, srcI = centroidStream.Intensities)
                    {
                        Buffer.MemoryCopy(srcM, pMasses, (long)count * sizeof(double), (long)count * sizeof(double));
                        Buffer.MemoryCopy(srcI, pIntensities, (long)count * sizeof(double), (long)count * sizeof(double));
                    }

                    outData->masses = pMasses;
                    outData->intensities = pIntensities;
                    outData->count = count;
                    return RAW_SUCCESS;
                }
            }
            catch
            {
                return RAW_ERROR_SCAN_OUT_OF_RANGE;
            }
        }


        #endregion

        #region File Lifecycle Unmanaged Exports

        [UnmanagedCallersOnly(EntryPoint = "raw_open_file")]
        public static int RawOpenFile(byte* filePathPtr, IntPtr* outHandle) => OpenFileCore(filePathPtr, outHandle);

        [UnmanagedCallersOnly(EntryPoint = "open_raw_file")]
        public static int OpenRawFileLegacy(byte* filePathPtr, IntPtr* outHandle) => OpenFileCore(filePathPtr, outHandle);

        [UnmanagedCallersOnly(EntryPoint = "raw_close_file")]
        public static void RawCloseFile(IntPtr fileHandle) => CloseFileCore(fileHandle);

        [UnmanagedCallersOnly(EntryPoint = "close_raw_file")]
        public static void CloseRawFileLegacy(IntPtr fileHandle) => CloseFileCore(fileHandle);

        #endregion

        #region Run / File Information Unmanaged Exports

        [UnmanagedCallersOnly(EntryPoint = "raw_get_first_scan")]
        public static int RawGetFirstScan(IntPtr fileHandle, int* outFirstScan)
        {
            if (outFirstScan == null) return RAW_ERROR_NULL_POINTER;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                lock (sessionLock!)
                {
                    *outFirstScan = rawFile!.RunHeaderEx.FirstSpectrum;
                }
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_last_scan")]
        public static int RawGetLastScan(IntPtr fileHandle, int* outLastScan) => GetLastScanCore(fileHandle, outLastScan);

        [UnmanagedCallersOnly(EntryPoint = "get_last_scan")]
        public static int GetLastScanLegacy(IntPtr fileHandle, int* outLastScan) => GetLastScanCore(fileHandle, outLastScan);

        [UnmanagedCallersOnly(EntryPoint = "raw_get_spectra_count")]
        public static int RawGetSpectraCount(IntPtr fileHandle, int* outCount)
        {
            if (outCount == null) return RAW_ERROR_NULL_POINTER;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                lock (sessionLock!)
                {
                    *outCount = rawFile!.RunHeaderEx.SpectraCount;
                }
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_start_time")]
        public static int RawGetStartTime(IntPtr fileHandle, double* outStartTime)
        {
            if (outStartTime == null) return RAW_ERROR_NULL_POINTER;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                lock (sessionLock!)
                {
                    *outStartTime = rawFile!.RunHeaderEx.StartTime;
                }
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_end_time")]
        public static int RawGetEndTime(IntPtr fileHandle, double* outEndTime)
        {
            if (outEndTime == null) return RAW_ERROR_NULL_POINTER;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                lock (sessionLock!)
                {
                    *outEndTime = rawFile!.RunHeaderEx.EndTime;
                }
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_mass_range")]
        public static int RawGetMassRange(IntPtr fileHandle, double* outLowMass, double* outHighMass)
        {
            if (outLowMass == null || outHighMass == null) return RAW_ERROR_NULL_POINTER;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                lock (sessionLock!)
                {
                    *outLowMass = rawFile!.RunHeaderEx.LowMass;
                    *outHighMass = rawFile!.RunHeaderEx.HighMass;
                }
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_instrument_model")]
        public static int RawGetInstrumentModel(IntPtr fileHandle, byte** outModelStr)
        {
            if (outModelStr == null) return RAW_ERROR_NULL_POINTER;
            *outModelStr = null;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                string model;
                lock (sessionLock!)
                {
                    var data = rawFile!.GetInstrumentData();
                    model = data.Model ?? string.Empty;
                }
                *outModelStr = AllocUtf8String(model);
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_instrument_name")]
        public static int RawGetInstrumentName(IntPtr fileHandle, byte** outNameStr)
        {
            if (outNameStr == null) return RAW_ERROR_NULL_POINTER;
            *outNameStr = null;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                string name;
                lock (sessionLock!)
                {
                    var data = rawFile!.GetInstrumentData();
                    name = data.Name ?? string.Empty;
                }
                *outNameStr = AllocUtf8String(name);
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_file_name")]
        public static int RawGetFileName(IntPtr fileHandle, byte** outFileNameStr)
        {
            if (outFileNameStr == null) return RAW_ERROR_NULL_POINTER;
            *outFileNameStr = null;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                string fileName;
                lock (sessionLock!)
                {
                    fileName = rawFile!.FileName ?? string.Empty;
                }
                *outFileNameStr = AllocUtf8String(fileName);
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_creation_date")]
        public static int RawGetCreationDate(IntPtr fileHandle, byte** outDateStr)
        {
            if (outDateStr == null) return RAW_ERROR_NULL_POINTER;
            *outDateStr = null;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                string dateStr;
                lock (sessionLock!)
                {
                    dateStr = rawFile!.FileHeader?.CreationDate.ToString("o") ?? string.Empty;
                }
                *outDateStr = AllocUtf8String(dateStr);
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_file_description")]
        public static int RawGetFileDescription(IntPtr fileHandle, byte** outDescStr)
        {
            if (outDescStr == null) return RAW_ERROR_NULL_POINTER;
            *outDescStr = null;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                string desc;
                lock (sessionLock!)
                {
                    desc = rawFile!.FileHeader?.FileDescription ?? string.Empty;
                }
                *outDescStr = AllocUtf8String(desc);
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_instrument_serial_number")]
        public static int RawGetInstrumentSerialNumber(IntPtr fileHandle, byte** outSerialStr)
        {
            if (outSerialStr == null) return RAW_ERROR_NULL_POINTER;
            *outSerialStr = null;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                string serial;
                lock (sessionLock!)
                {
                    var data = rawFile!.GetInstrumentData();
                    serial = data.SerialNumber ?? string.Empty;
                }
                *outSerialStr = AllocUtf8String(serial);
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_instrument_software_version")]
        public static int RawGetInstrumentSoftwareVersion(IntPtr fileHandle, byte** outVersionStr)
        {
            if (outVersionStr == null) return RAW_ERROR_NULL_POINTER;
            *outVersionStr = null;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                string version;
                lock (sessionLock!)
                {
                    var data = rawFile!.GetInstrumentData();
                    version = data.SoftwareVersion ?? string.Empty;
                }
                *outVersionStr = AllocUtf8String(version);
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_instrument_hardware_version")]
        public static int RawGetInstrumentHardwareVersion(IntPtr fileHandle, byte** outVersionStr)
        {
            if (outVersionStr == null) return RAW_ERROR_NULL_POINTER;
            *outVersionStr = null;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                string version;
                lock (sessionLock!)
                {
                    var data = rawFile!.GetInstrumentData();
                    version = data.HardwareVersion ?? string.Empty;
                }
                *outVersionStr = AllocUtf8String(version);
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_mass_resolution")]
        public static int RawGetMassResolution(IntPtr fileHandle, double* outResolution)
        {
            if (outResolution == null) return RAW_ERROR_NULL_POINTER;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                lock (sessionLock!)
                {
                    *outResolution = rawFile!.RunHeaderEx.MassResolution;
                }
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_instrument_count")]
        public static int RawGetInstrumentCount(IntPtr fileHandle, int* outCount)
        {
            if (outCount == null) return RAW_ERROR_NULL_POINTER;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                lock (sessionLock!)
                {
                    *outCount = rawFile!.InstrumentCount;
                }
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_filter_count")]
        public static int RawGetFilterCount(IntPtr fileHandle, int* outCount)
        {
            if (outCount == null) return RAW_ERROR_NULL_POINTER;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                lock (sessionLock!)
                {
                    *outCount = rawFile!.GetFilters().Count;
                }
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_sample_name")]
        public static int RawGetSampleName(IntPtr fileHandle, byte** outStr)
        {
            if (outStr == null) return RAW_ERROR_NULL_POINTER;
            *outStr = null;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                string s;
                lock (sessionLock!)
                {
                    s = rawFile!.SampleInformation?.SampleName ?? string.Empty;
                }
                *outStr = AllocUtf8String(s);
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_sample_id")]
        public static int RawGetSampleId(IntPtr fileHandle, byte** outStr)
        {
            if (outStr == null) return RAW_ERROR_NULL_POINTER;
            *outStr = null;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                string s;
                lock (sessionLock!)
                {
                    s = rawFile!.SampleInformation?.SampleId ?? string.Empty;
                }
                *outStr = AllocUtf8String(s);
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_sample_comment")]
        public static int RawGetSampleComment(IntPtr fileHandle, byte** outStr)
        {
            if (outStr == null) return RAW_ERROR_NULL_POINTER;
            *outStr = null;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                string s;
                lock (sessionLock!)
                {
                    s = rawFile!.SampleInformation?.Comment ?? string.Empty;
                }
                *outStr = AllocUtf8String(s);
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_sample_vial")]
        public static int RawGetSampleVial(IntPtr fileHandle, byte** outStr)
        {
            if (outStr == null) return RAW_ERROR_NULL_POINTER;
            *outStr = null;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                string s;
                lock (sessionLock!)
                {
                    s = rawFile!.SampleInformation?.Vial ?? string.Empty;
                }
                *outStr = AllocUtf8String(s);
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        #endregion

        #region Scan Metadata Unmanaged Exports

        [UnmanagedCallersOnly(EntryPoint = "raw_get_scan_retention_time")]
        public static int RawGetScanRetentionTime(IntPtr fileHandle, int scanNumber, double* outRt)
        {
            if (outRt == null) return RAW_ERROR_NULL_POINTER;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                lock (sessionLock!)
                {
                    *outRt = rawFile!.RetentionTimeFromScanNumber(scanNumber);
                }
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_SCAN_OUT_OF_RANGE;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_scan_number_from_retention_time")]
        public static int RawGetScanNumberFromRetentionTime(IntPtr fileHandle, double retentionTime, int* outScan)
        {
            if (outScan == null) return RAW_ERROR_NULL_POINTER;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                lock (sessionLock!)
                {
                    *outScan = rawFile!.ScanNumberFromRetentionTime(retentionTime);
                }
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }


        [UnmanagedCallersOnly(EntryPoint = "raw_get_scan_stats")]
        public static int RawGetScanStats(IntPtr fileHandle, int scanNumber, RawScanStats* outStats)
        {
            if (outStats == null) return RAW_ERROR_NULL_POINTER;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                int msLevel = 1;
                int polarity = 0;
                int isCentroid = 0;
                int packetCount = 0;
                double rt = 0.0;
                double tic = 0.0;
                double bpMass = 0.0;
                double bpInt = 0.0;
                double lowM = 0.0;
                double highM = 0.0;

                lock (sessionLock!)
                {
                    var stats = rawFile!.GetScanStatsForScanNumber(scanNumber);
                    var filter = rawFile.GetFilterForScanNumber(scanNumber);

                    msLevel = filter != null ? (int)filter.MSOrder : 1;
                    polarity = filter != null
                        ? (filter.Polarity == PolarityType.Positive ? 1 : filter.Polarity == PolarityType.Negative ? 2 : 0)
                        : 0;
                    isCentroid = stats.IsCentroidScan ? 1 : 0;
                    packetCount = stats.PacketCount;
                    rt = stats.StartTime;
                    tic = stats.TIC;
                    bpMass = stats.BasePeakMass;
                    bpInt = stats.BasePeakIntensity;
                    lowM = stats.LowMass;
                    highM = stats.HighMass;
                }

                outStats->scan_number = scanNumber;
                outStats->ms_level = msLevel;
                outStats->polarity = polarity;
                outStats->is_centroid = isCentroid;
                outStats->packet_count = packetCount;
                outStats->retention_time = rt;
                outStats->tic = tic;
                outStats->base_peak_mass = bpMass;
                outStats->base_peak_intensity = bpInt;
                outStats->low_mass = lowM;
                outStats->high_mass = highM;

                return RAW_SUCCESS;
            }

            catch
            {
                return RAW_ERROR_SCAN_OUT_OF_RANGE;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_scan_filter")]
        public static int RawGetScanFilter(IntPtr fileHandle, int scanNumber, byte** outFilterStr)
        {
            if (outFilterStr == null) return RAW_ERROR_NULL_POINTER;
            *outFilterStr = null;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                string? filterStr = null;
                lock (sessionLock!)
                {
                    try
                    {
                        filterStr = rawFile!.GetScanEventStringForScanNumber(scanNumber);
                    }
                    catch
                    {
                        // Fall back to filter object
                    }

                    if (string.IsNullOrEmpty(filterStr))
                    {
                        var filter = rawFile!.GetFilterForScanNumber(scanNumber);
                        if (filter != null)
                        {
                            filterStr = filter.ToString() ?? filter.Name;
                        }
                    }
                }

                if (string.IsNullOrEmpty(filterStr)) return RAW_ERROR_NO_DATA;

                *outFilterStr = AllocUtf8String(filterStr);
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_SCAN_OUT_OF_RANGE;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_precursor_info")]
        public static int RawGetPrecursorInfo(IntPtr fileHandle, int scanNumber, RawPrecursorInfo* outInfo)
        {
            if (outInfo == null) return RAW_ERROR_NULL_POINTER;
            *outInfo = default;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                lock (sessionLock!)
                {
                    var filter = rawFile!.GetFilterForScanNumber(scanNumber);
                    if (filter == null || filter.MSOrder <= MSOrderType.Ms)
                    {
                        return RAW_ERROR_NO_PRECURSOR_INFO;
                    }

                    // 1. Try reading from IScanEvent (official source for precursor masses and isolation widths)
                    try
                    {
                        var scanEvent = rawFile.GetScanEventForScanNumber(scanNumber);
                        if (scanEvent != null)
                        {
                            if (scanEvent.MassCount > 0)
                            {
                                int lastIdx = scanEvent.MassCount - 1;
                                outInfo->precursor_mass = scanEvent.GetMass(lastIdx);
                                outInfo->isolation_width = scanEvent.GetIsolationWidth(lastIdx);
                                if (outInfo->isolation_width <= 0.0 && lastIdx > 0)
                                {
                                    outInfo->isolation_width = scanEvent.GetIsolationWidth(0);
                                }
                            }
                            try
                            {
                                var rx0 = scanEvent.GetReaction(0);
                                if (rx0 != null)
                                {
                                    outInfo->collision_energy = rx0.CollisionEnergy;
                                    if (outInfo->isolation_width <= 0.0) outInfo->isolation_width = rx0.IsolationWidth;
                                    if (outInfo->precursor_mass <= 0.0) outInfo->precursor_mass = rx0.PrecursorMass;
                                }
                                if (scanEvent.MassCount > 1)
                                {
                                    var rxLast = scanEvent.GetReaction(scanEvent.MassCount - 1);
                                    if (rxLast != null)
                                    {
                                        if (rxLast.CollisionEnergy > 0.0) outInfo->collision_energy = rxLast.CollisionEnergy;
                                        if (rxLast.IsolationWidth > 0.0) outInfo->isolation_width = rxLast.IsolationWidth;
                                        if (rxLast.PrecursorMass > 0.0) outInfo->precursor_mass = rxLast.PrecursorMass;
                                    }
                                }
                            }
                            catch
                            {
                                // Reactions optional
                            }
                        }
                    }
                    catch
                    {
                        // ScanEvent optional
                    }

                    // 2. Try reading metadata from trailer extra
                    try
                    {
                        var trailer = rawFile.GetTrailerExtraInformation(scanNumber);
                        if (trailer != null && trailer.Labels != null && trailer.Values != null)
                        {
                            for (int i = 0; i < trailer.Length; i++)
                            {
                                string lbl = trailer.Labels[i];
                                string val = trailer.Values[i];

                                if (lbl.StartsWith("Master Scan", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (int.TryParse(val, out int ms))
                                    {
                                        outInfo->master_scan = ms;
                                    }
                                }
                                else if (lbl.StartsWith("Charge State", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (int.TryParse(val, out int charge))
                                    {
                                        outInfo->charge_state = charge;
                                    }
                                }
                                else if (lbl.StartsWith("Monoisotopic M/Z", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double monoMz))
                                    {
                                        outInfo->monoisotopic_mass = monoMz;
                                    }
                                }
                                else if (lbl.Contains("Isolation Width", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (outInfo->isolation_width <= 0.0 && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double isoW))
                                    {
                                        outInfo->isolation_width = isoW;
                                    }
                                }
                                else if (lbl.Contains("Collision Energy", StringComparison.OrdinalIgnoreCase) || lbl.Contains("HCD Energy", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (outInfo->collision_energy <= 0.0 && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double ce))
                                    {
                                        outInfo->collision_energy = ce;
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Trailer extra data optional
                    }

                    // 3. Try parsing precursor m/z and collision energy from the scan filter string (e.g., "... ms3 451.80@cid35.00 670.21@hcd65.00 ...")
                    try
                    {
                        string? filterStr = rawFile.GetScanEventStringForScanNumber(scanNumber);
                        if (!string.IsNullOrEmpty(filterStr))
                        {
                            int lastAt = filterStr.LastIndexOf('@');
                            if (lastAt > 0)
                            {
                                int spaceBefore = filterStr.LastIndexOf(' ', lastAt);
                                int start = spaceBefore >= 0 ? spaceBefore + 1 : 0;
                                string precStr = filterStr.Substring(start, lastAt - start);
                                if (double.TryParse(precStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedPrec))
                                {
                                    outInfo->precursor_mass = parsedPrec;
                                }

                                if (outInfo->collision_energy <= 0.0 && lastAt + 1 < filterStr.Length)
                                {
                                    string afterAt = filterStr.Substring(lastAt + 1);
                                    int ceStart = 0;
                                    while (ceStart < afterAt.Length && char.IsLetter(afterAt[ceStart])) ceStart++;
                                    int ceEnd = ceStart;
                                    while (ceEnd < afterAt.Length && (char.IsDigit(afterAt[ceEnd]) || afterAt[ceEnd] == '.')) ceEnd++;
                                    if (ceEnd > ceStart && double.TryParse(afterAt.Substring(ceStart, ceEnd - ceStart), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedCe))
                                    {
                                        outInfo->collision_energy = parsedCe;
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Filter parsing optional
                    }

                // 3. Fall back to ScanDependents if precursor_mass is still not found
                if (outInfo->precursor_mass == 0.0)
                {
                    try
                    {
                        var dependents = rawFile.GetScanDependents(scanNumber, 2);
                        if (dependents?.ScanDependentDetailArray != null && dependents.ScanDependentDetailArray.Length > 0)
                        {
                            var detail = dependents.ScanDependentDetailArray[0];
                            if (outInfo->master_scan == 0) outInfo->master_scan = detail.ScanIndex;
                            if (detail.PrecursorMassArray != null && detail.PrecursorMassArray.Length > 0)
                            {
                                outInfo->precursor_mass = detail.PrecursorMassArray[0];
                            }
                            if (detail.IsolationWidthArray != null && detail.IsolationWidthArray.Length > 0)
                            {
                                outInfo->isolation_width = detail.IsolationWidthArray[0];
                            }
                        }
                    }
                    catch
                    {
                        // Dependents optional
                    }
                }

                // If we found any precursor or monoisotopic mass, consider it success
                if (outInfo->precursor_mass > 0.0 || outInfo->monoisotopic_mass > 0.0)
                {
                    return RAW_SUCCESS;
                }

                return RAW_ERROR_NO_PRECURSOR_INFO;
                }
            }
            catch

            {
                return RAW_ERROR_SCAN_OUT_OF_RANGE;
            }
        }

        #endregion

        #region Trailer Extra Unmanaged Exports

        [UnmanagedCallersOnly(EntryPoint = "raw_get_trailer_extra_count")]
        public static int RawGetTrailerExtraCount(IntPtr fileHandle, int scanNumber, int* outCount)
        {
            if (outCount == null) return RAW_ERROR_NULL_POINTER;
            *outCount = 0;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                lock (sessionLock!)
                {
                    var trailer = rawFile!.GetTrailerExtraInformation(scanNumber);
                    if (trailer != null)
                    {
                        *outCount = trailer.Length;
                        return RAW_SUCCESS;
                    }
                }
                return RAW_ERROR_NO_DATA;
            }
            catch
            {
                return RAW_ERROR_SCAN_OUT_OF_RANGE;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_trailer_extra_item")]
        public static int RawGetTrailerExtraItem(IntPtr fileHandle, int scanNumber, int index, byte** outLabel, byte** outValue)
        {
            if (outLabel == null || outValue == null) return RAW_ERROR_NULL_POINTER;
            *outLabel = null;
            *outValue = null;
            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                string? lbl = null;
                string? val = null;
                lock (sessionLock!)
                {
                    var trailer = rawFile!.GetTrailerExtraInformation(scanNumber);
                    if (trailer == null || index < 0 || index >= trailer.Length)
                    {
                        return RAW_ERROR_SCAN_OUT_OF_RANGE;
                    }
                    lbl = trailer.Labels[index];
                    val = trailer.Values[index];
                }

                *outLabel = AllocUtf8String(lbl);
                *outValue = AllocUtf8String(val);
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_SCAN_OUT_OF_RANGE;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_trailer_extra_value_for_label")]
        public static int RawGetTrailerExtraValueForLabel(IntPtr fileHandle, int scanNumber, byte* labelPtr, byte** outValue)
        {
            if (labelPtr == null || outValue == null) return RAW_ERROR_NULL_POINTER;
            *outValue = null;
            if (labelPtr == null) return RAW_ERROR_NULL_POINTER;
            string? label = Marshal.PtrToStringUTF8((IntPtr)labelPtr);
            if (string.IsNullOrEmpty(label)) return RAW_ERROR_NULL_POINTER;

            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                string? foundVal = null;
                lock (sessionLock!)
                {
                    var trailer = rawFile!.GetTrailerExtraInformation(scanNumber);
                    if (trailer != null)
                    {
                        for (int i = 0; i < trailer.Length; i++)
                        {
                            if (trailer.Labels[i].StartsWith(label, StringComparison.OrdinalIgnoreCase))
                            {
                                foundVal = trailer.Values[i];
                                break;
                            }
                        }
                    }
                }

                if (foundVal == null) return RAW_ERROR_NO_DATA;

                *outValue = AllocUtf8String(foundVal);
                return RAW_SUCCESS;
            }
            catch
            {
                return RAW_ERROR_SCAN_OUT_OF_RANGE;
            }
        }


        #endregion

        #region Spectrum Data Unmanaged Exports

        [UnmanagedCallersOnly(EntryPoint = "raw_get_segmented_scan")]
        public static int RawGetSegmentedScan(IntPtr fileHandle, int scanNumber, RawSpectrumData* outData)
            => GetSegmentedScanCore(fileHandle, scanNumber, outData);

        [UnmanagedCallersOnly(EntryPoint = "raw_get_centroid_stream")]
        public static int RawGetCentroidStream(IntPtr fileHandle, int scanNumber, RawSpectrumData* outData)
            => GetCentroidStreamCore(fileHandle, scanNumber, outData);

        [UnmanagedCallersOnly(EntryPoint = "raw_get_best_spectrum")]
        public static int RawGetBestSpectrum(IntPtr fileHandle, int scanNumber, RawSpectrumData* outData, int* outIsCentroid)
        {
            if (outData == null || outIsCentroid == null) return RAW_ERROR_NULL_POINTER;
            *outIsCentroid = 0;

            // Attempt to fetch centroid stream first
            int err = GetCentroidStreamCore(fileHandle, scanNumber, outData);
            if (err == RAW_SUCCESS && outData->count > 0)
            {
                *outIsCentroid = 1;
                return RAW_SUCCESS;
            }

            // Fall back to segmented scan
            err = GetSegmentedScanCore(fileHandle, scanNumber, outData);
            if (err == RAW_SUCCESS)
            {
                *outIsCentroid = 0;
                return RAW_SUCCESS;
            }

            return err;
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_get_average_spectrum")]
        public static int RawGetAverageSpectrum(IntPtr fileHandle, int firstScan, int lastScan, RawSpectrumData* outData, int* outIsCentroid)
        {
            if (outData == null || outIsCentroid == null) return RAW_ERROR_NULL_POINTER;
            outData->masses = null;
            outData->intensities = null;
            outData->count = 0;
            *outIsCentroid = 0;

            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                lock (sessionLock!)
                {
                    int first = firstScan > 0 ? firstScan : rawFile!.RunHeaderEx.FirstSpectrum;
                    int last = lastScan > 0 ? lastScan : rawFile!.RunHeaderEx.LastSpectrum;

                    var options = rawFile!.DefaultMassOptions();
                    options.ToleranceUnits = ToleranceUnits.ppm;
                    options.Tolerance = 5.0;

                    var scanFilter = rawFile.GetFilterForScanNumber(first);
                    if (scanFilter == null) return RAW_ERROR_NO_DATA;
                    var averager = ScanAveragerPlus.FromFile((IDetectorReaderPlus)rawFile);
                    var avgScan = averager.AverageScansInScanRange(first, last, scanFilter, options);
                    if (avgScan == null) return RAW_ERROR_NO_DATA;

                    double[]? masses = null;
                    double[]? intensities = null;
                    bool isCentroid = false;

                    if (avgScan.HasCentroidStream && avgScan.CentroidScan != null && avgScan.CentroidScan.Length > 0)
                    {
                        masses = avgScan.CentroidScan.Masses;
                        intensities = avgScan.CentroidScan.Intensities;
                        isCentroid = true;
                    }
                    else if (avgScan.PreferredMasses != null && avgScan.PreferredMasses.Length > 0)
                    {
                        masses = avgScan.PreferredMasses;
                        intensities = avgScan.PreferredIntensities;
                        isCentroid = false;
                    }
                    else if (avgScan.SegmentedScan != null && avgScan.SegmentedScan.Positions != null && avgScan.SegmentedScan.Positions.Length > 0)
                    {
                        masses = avgScan.SegmentedScan.Positions;
                        intensities = avgScan.SegmentedScan.Intensities;
                        isCentroid = false;
                    }

                    if (masses == null || intensities == null || masses.Length == 0)
                    {
                        return RAW_SUCCESS;
                    }

                    int count = Math.Min(masses.Length, intensities.Length);
                    double* pMasses = (double*)NativeMemory.Alloc((nuint)count, (nuint)sizeof(double));
                    double* pIntensities = (double*)NativeMemory.Alloc((nuint)count, (nuint)sizeof(double));

                    fixed (double* srcM = masses, srcI = intensities)
                    {
                        Buffer.MemoryCopy(srcM, pMasses, (long)count * sizeof(double), (long)count * sizeof(double));
                        Buffer.MemoryCopy(srcI, pIntensities, (long)count * sizeof(double), (long)count * sizeof(double));
                    }

                    outData->masses = pMasses;
                    outData->intensities = pIntensities;
                    outData->count = count;
                    *outIsCentroid = isCentroid ? 1 : 0;
                    return RAW_SUCCESS;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("RawGetAverageSpectrum error: " + ex + (ex.InnerException != null ? "\nInner: " + ex.InnerException : ""));
                return RAW_ERROR_UNEXPECTED;
            }
        }

        #endregion

        #region Chromatogram Unmanaged Exports

        [UnmanagedCallersOnly(EntryPoint = "raw_get_chromatogram")]
        public static int RawGetChromatogram(IntPtr fileHandle, int traceType, int startScan, int endScan, byte* filterPtr, RawChromatogramData* outData)
        {
            if (outData == null) return RAW_ERROR_NULL_POINTER;
            outData->times = null;
            outData->intensities = null;
            outData->count = 0;

            if (!TryGetRawFile(fileHandle, out var rawFile, out var sessionLock, out int err)) return err;

            try
            {
                string? filter = filterPtr != null ? Marshal.PtrToStringUTF8((IntPtr)filterPtr) : null;
                lock (sessionLock!)
                {
                    int first = startScan > 0 ? startScan : rawFile!.RunHeaderEx.FirstSpectrum;
                    int last = endScan > 0 ? endScan : rawFile!.RunHeaderEx.LastSpectrum;

                    TraceType tt = traceType switch
                    {
                        1 => TraceType.TIC,
                        2 => TraceType.MassRange,
                        _ => TraceType.BasePeak
                    };

                    ChromatogramTraceSettings settings = new ChromatogramTraceSettings(tt);
                    if (!string.IsNullOrEmpty(filter))
                    {
                        settings.Filter = filter;
                    }

                    var data = rawFile!.GetChromatogramData(new IChromatogramSettings[] { settings }, first, last);
                    if (data == null) return RAW_ERROR_NO_DATA;

                    var traces = ChromatogramSignal.FromChromatogramData(data);
                    if (traces == null || traces.Length == 0 || traces[0] == null)
                    {
                        return RAW_ERROR_NO_DATA;
                    }

                    var trace = traces[0];
                    int count = trace.Length;
                    if (count == 0 || trace.Times == null || trace.Intensities == null)
                    {
                        return RAW_SUCCESS;
                    }

                    double* pTimes = (double*)NativeMemory.Alloc((nuint)count, (nuint)sizeof(double));
                    double* pIntensities = (double*)NativeMemory.Alloc((nuint)count, (nuint)sizeof(double));

                    for (int i = 0; i < count; i++)
                    {
                        pTimes[i] = trace.Times[i];
                        pIntensities[i] = trace.Intensities[i];
                    }

                    outData->times = pTimes;
                    outData->intensities = pIntensities;
                    outData->count = count;
                    return RAW_SUCCESS;
                }
            }
            catch
            {
                return RAW_ERROR_UNEXPECTED;
            }
        }

        #endregion

        #region Memory Cleanup Unmanaged Exports

        [UnmanagedCallersOnly(EntryPoint = "raw_free_string")]
        public static void RawFreeString(byte* str)
        {
            if (str != null)
            {
                NativeMemory.Free(str);
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_free_spectrum_data")]
        public static void RawFreeSpectrumData(RawSpectrumData* data)
        {
            if (data == null) return;
            if (data->masses != null)
            {
                NativeMemory.Free(data->masses);
                data->masses = null;
            }
            if (data->intensities != null)
            {
                NativeMemory.Free(data->intensities);
                data->intensities = null;
            }
            data->count = 0;
        }

        [UnmanagedCallersOnly(EntryPoint = "raw_free_chromatogram_data")]
        public static void RawFreeChromatogramData(RawChromatogramData* data)
        {
            if (data == null) return;
            if (data->times != null)
            {
                NativeMemory.Free(data->times);
                data->times = null;
            }
            if (data->intensities != null)
            {
                NativeMemory.Free(data->intensities);
                data->intensities = null;
            }
            data->count = 0;
        }

        #endregion
    }
}
