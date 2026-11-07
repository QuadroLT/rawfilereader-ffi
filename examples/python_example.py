#!/usr/bin/env python3
"""
Python example for ThermoRawFileReader C ABI
===========================================
Demonstrates using the standalone native library (librawfilereader) directly
from Python using the standard library `ctypes` module (zero external dependencies).

Usage:
    python3 examples/python_example.py [path_to_raw_file]
"""
from __future__ import annotations

import sys
import os
import platform
import ctypes
from ctypes import (
    c_void_p, c_char_p, c_int32, c_double, POINTER, Structure, byref
)

# ---------------------------------------------------------------------------
# C Structures matching `include/rawfilereader.h`
# ---------------------------------------------------------------------------

class RawSpectrumData(Structure):
    _fields_ = [
        ("masses", POINTER(c_double)),
        ("intensities", POINTER(c_double)),
        ("count", c_int32),
    ]

class RawScanStats(Structure):
    _fields_ = [
        ("scan_number", c_int32),
        ("ms_level", c_int32),
        ("polarity", c_int32),
        ("is_centroid", c_int32),
        ("packet_count", c_int32),
        ("retention_time", c_double),
        ("tic", c_double),
        ("base_peak_mass", c_double),
        ("base_peak_intensity", c_double),
        ("low_mass", c_double),
        ("high_mass", c_double),
    ]

class RawPrecursorInfo(Structure):
    _fields_ = [
        ("master_scan", c_int32),
        ("precursor_mass", c_double),
        ("isolation_width", c_double),
        ("charge_state", c_int32),
        ("monoisotopic_mass", c_double),
        ("collision_energy", c_double),
    ]

class RawChromatogramData(Structure):
    _fields_ = [
        ("times", POINTER(c_double)),
        ("intensities", POINTER(c_double)),
        ("count", c_int32),
    ]

# ---------------------------------------------------------------------------
# Library Loader
# ---------------------------------------------------------------------------

def find_library():
    """Locate librawfilereader based on OS and common directory locations."""
    system = platform.system()
    if system == "Darwin":
        lib_name = "librawfilereader.dylib"
    elif system == "Windows":
        lib_name = "librawfilereader.dll"
    else:
        lib_name = "librawfilereader.so"

    search_dirs = [
        os.path.join(os.path.dirname(__file__), "..", "lib"),
        os.path.join(os.path.dirname(__file__), "lib"),
        os.path.join(os.getcwd(), "lib"),
        os.path.join(os.getcwd(), "rawfilereader-ffi", "lib"),
        os.path.dirname(__file__),
    ]

    for d in search_dirs:
        candidate = os.path.abspath(os.path.join(d, lib_name))
        if os.path.exists(candidate):
            return candidate

    return lib_name

def load_rawfilereader():
    lib_path = find_library()
    try:
        lib = ctypes.CDLL(lib_path)
    except OSError as e:
        print(f"Error loading {lib_path}: {e}")
        print("Please build the library first: dotnet publish RawReaderAdapter -c Release")
        sys.exit(1)

    # File lifecycle
    lib.raw_open_file.argtypes = [c_char_p, POINTER(c_void_p)]
    lib.raw_open_file.restype = c_int32

    lib.raw_close_file.argtypes = [c_void_p]
    lib.raw_close_file.restype = None

    # File / run metadata
    lib.raw_get_first_scan.argtypes = [c_void_p, POINTER(c_int32)]
    lib.raw_get_first_scan.restype = c_int32

    lib.raw_get_last_scan.argtypes = [c_void_p, POINTER(c_int32)]
    lib.raw_get_last_scan.restype = c_int32

    lib.raw_get_spectra_count.argtypes = [c_void_p, POINTER(c_int32)]
    lib.raw_get_spectra_count.restype = c_int32

    lib.raw_get_start_time.argtypes = [c_void_p, POINTER(c_double)]
    lib.raw_get_start_time.restype = c_int32

    lib.raw_get_end_time.argtypes = [c_void_p, POINTER(c_double)]
    lib.raw_get_end_time.restype = c_int32

    lib.raw_get_instrument_model.argtypes = [c_void_p, POINTER(c_char_p)]
    lib.raw_get_instrument_model.restype = c_int32

    # Scan metadata
    lib.raw_get_scan_stats.argtypes = [c_void_p, c_int32, POINTER(RawScanStats)]
    lib.raw_get_scan_stats.restype = c_int32

    lib.raw_get_scan_filter.argtypes = [c_void_p, c_int32, POINTER(c_char_p)]
    lib.raw_get_scan_filter.restype = c_int32

    lib.raw_get_precursor_info.argtypes = [c_void_p, c_int32, POINTER(RawPrecursorInfo)]
    lib.raw_get_precursor_info.restype = c_int32

    lib.raw_get_trailer_extra_value_for_label.argtypes = [c_void_p, c_int32, c_char_p, POINTER(c_char_p)]
    lib.raw_get_trailer_extra_value_for_label.restype = c_int32

    # Spectrum data
    lib.raw_get_best_spectrum.argtypes = [c_void_p, c_int32, POINTER(RawSpectrumData), POINTER(c_int32)]
    lib.raw_get_best_spectrum.restype = c_int32

    # Memory cleanup
    lib.raw_free_string.argtypes = [c_char_p]
    lib.raw_free_string.restype = None

    lib.raw_free_spectrum_data.argtypes = [POINTER(RawSpectrumData)]
    lib.raw_free_spectrum_data.restype = None

    lib.raw_free_chromatogram_data.argtypes = [POINTER(RawChromatogramData)]
    lib.raw_free_chromatogram_data.restype = None

    return lib

# ---------------------------------------------------------------------------
# High-Level Python Wrapper
# ---------------------------------------------------------------------------

class RawFileReader:
    """Convenient, Pythonic context-manager wrapper around RawFileHandle."""

    def __init__(self, file_path: str):
        self.lib = load_rawfilereader()
        self.file_path = file_path
        self.handle = c_void_p()
        ret = self.lib.raw_open_file(file_path.encode('utf-8'), byref(self.handle))
        if ret != 0 or not self.handle:
            raise RuntimeError(f"Failed to open RAW file '{file_path}' (error code: {ret})")

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        self.close()

    def close(self):
        if self.handle:
            self.lib.raw_close_file(self.handle)
            self.handle = None

    @property
    def first_scan(self) -> int:
        val = c_int32()
        self.lib.raw_get_first_scan(self.handle, byref(val))
        return val.value

    @property
    def last_scan(self) -> int:
        val = c_int32()
        self.lib.raw_get_last_scan(self.handle, byref(val))
        return val.value

    @property
    def spectra_count(self) -> int:
        val = c_int32()
        self.lib.raw_get_spectra_count(self.handle, byref(val))
        return val.value

    @property
    def time_range(self) -> tuple[float, float]:
        start = c_double()
        end = c_double()
        self.lib.raw_get_start_time(self.handle, byref(start))
        self.lib.raw_get_end_time(self.handle, byref(end))
        return (start.value, end.value)

    @property
    def instrument_model(self) -> str:
        ptr = c_char_p()
        if self.lib.raw_get_instrument_model(self.handle, byref(ptr)) == 0 and ptr.value:
            s = ptr.value.decode('utf-8')
            self.lib.raw_free_string(ptr)
            return s
        return "Unknown"

    def get_scan_stats(self, scan: int) -> RawScanStats:
        stats = RawScanStats()
        ret = self.lib.raw_get_scan_stats(self.handle, scan, byref(stats))
        if ret != 0:
            raise KeyError(f"Failed to get stats for scan {scan} (code {ret})")
        return stats

    def get_scan_filter(self, scan: int) -> str:
        ptr = c_char_p()
        if self.lib.raw_get_scan_filter(self.handle, scan, byref(ptr)) == 0 and ptr.value:
            s = ptr.value.decode('utf-8')
            self.lib.raw_free_string(ptr)
            return s
        return ""

    def get_precursor_info(self, scan: int) -> RawPrecursorInfo | None:
        info = RawPrecursorInfo()
        ret = self.lib.raw_get_precursor_info(self.handle, scan, byref(info))
        if ret == 0:
            return info
        return None

    def get_trailer_extra(self, scan: int, label: str) -> str | None:
        ptr = c_char_p()
        ret = self.lib.raw_get_trailer_extra_value_for_label(
            self.handle, scan, label.encode('utf-8'), byref(ptr)
        )
        if ret == 0 and ptr.value:
            s = ptr.value.decode('utf-8')
            self.lib.raw_free_string(ptr)
            return s
        return None

    def get_spectrum(self, scan: int) -> tuple[list[float], list[float], bool]:
        """Returns (masses, intensities, is_centroid) for the scan."""
        spec = RawSpectrumData()
        is_centroid = c_int32()
        ret = self.lib.raw_get_best_spectrum(self.handle, scan, byref(spec), byref(is_centroid))
        if ret != 0:
            raise RuntimeError(f"Failed to read spectrum for scan {scan} (code {ret})")

        masses = [spec.masses[i] for i in range(spec.count)]
        intensities = [spec.intensities[i] for i in range(spec.count)]
        self.lib.raw_free_spectrum_data(byref(spec))
        return masses, intensities, bool(is_centroid.value)

# ---------------------------------------------------------------------------
# Main Demo
# ---------------------------------------------------------------------------

def main():
    raw_path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.path.dirname(__file__), "dda_tmt_example.raw"
    )

    if not os.path.exists(raw_path):
        print(f"RAW file not found: {raw_path}")
        print("Please specify a valid Thermo .raw file path.")
        sys.exit(1)

    print("=== ThermoRawFileReader Python ctypes Demo ===")
    print(f"Opening: {raw_path}")

    with RawFileReader(raw_path) as reader:
        start_t, end_t = reader.time_range
        print(f"Instrument:   {reader.instrument_model}")
        print(f"Scans:        {reader.first_scan} to {reader.last_scan} ({reader.spectra_count} total)")
        print(f"Time Range:   {start_t:.2f} - {end_t:.2f} min")

        # Inspect scans across MS levels
        found_ms = {}
        for s in range(reader.first_scan, reader.last_scan + 1):
            stats = reader.get_scan_stats(s)
            ms_lvl = stats.ms_level
            if ms_lvl not in found_ms:
                found_ms[ms_lvl] = s
            if len(found_ms) >= 3:
                break

        for ms_lvl, scan_num in sorted(found_ms.items()):
            stats = reader.get_scan_stats(scan_num)
            filter_str = reader.get_scan_filter(scan_num)
            masses, intensities, is_centroid = reader.get_spectrum(scan_num)
            mode = "Centroid" if is_centroid else "Profile"

            print(f"\n--- MS{ms_lvl} Scan {scan_num} ---")
            print(f"  RT:         {stats.retention_time:.3f} min")
            print(f"  TIC:        {stats.tic:.2e}")
            print(f"  Filter:     {filter_str}")
            print(f"  Spectrum:   {len(masses)} peaks ({mode})")

            precursor = reader.get_precursor_info(scan_num)
            if precursor and precursor.precursor_mass > 0:
                print(f"  Precursor:  m/z {precursor.precursor_mass:.4f} (width: {precursor.isolation_width:.2f}, z: {precursor.charge_state})")
                if precursor.master_scan > 0:
                    print(f"  MasterScan: {precursor.master_scan}")

            if ms_lvl == 3:
                sps = reader.get_trailer_extra(scan_num, "SPS Masses")
                if sps:
                    print(f"  SPS Notches: {sps.strip()}")
                tmt_peaks = [(m, i) for m, i in zip(masses, intensities) if 126.0 <= m <= 135.0]
                if tmt_peaks:
                    print(f"  TMT Reporter Ions ({len(tmt_peaks)} peaks found):")
                    for m, i in tmt_peaks[:6]:
                        print(f"    m/z {m:8.4f} : intensity {i:10.1f}")

    print("\nFile closed cleanly.")

if __name__ == "__main__":
    main()
