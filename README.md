# ThermoRawFileReader C ABI & Shared Library

A zero-external-dependency, high-performance native shared library (`librawfilereader`) and C ABI interface for reading Thermo Fisher `.RAW` mass spectrometry files across multiple programming languages.

Built with .NET NativeAOT, the compiled library is **100% self-contained**: consumers do **not** need the .NET runtime installed.

---

## Table of Contents
- [Architecture & Layout](#architecture--layout)
- [Building the Shared Library](#building-the-shared-library)
- [C ABI Header (`rawfilereader.h`)](#c-abi-header-rawfilereaderh)
- [Using Outside Rust](#using-outside-rust)
  - [1. C / C++](#1-c--c)
  - [2. Python (Zero External Dependencies)](#2-python-zero-external-dependencies)
  - [3. Haskell (Extensive Either & Maybe)](#3-haskell)
  - [4. C# / .NET (P/Invoke)](#4-c--net-pinvoke)
  - [5. Julia (`ccall`)](#5-julia-ccall)
  - [6. R (`dyn.load`)](#6-r-dynload)
- [Memory Management Rules](#memory-management-rules)
- [Thread Safety & Parallel Processing](#thread-safety--parallel-processing)
- [Error Codes](#error-codes)

---

## Architecture & Layout

```
rawfilereader-ffi/
├── include/
│   └── rawfilereader.h       # Universal C ABI header file
├── lib/
│   └── librawfilereader.dylib # Standalone native shared library (.so / .dll / .dylib)
├── examples/
│   ├── c_example.c           # Pure C example (compiles with clang/gcc)
│   ├── python_example.py     # Pure Python ctypes example (zero dependencies)
│   └── haskell_example.hs    # Pure Haskell FFI example
│
├── RawReaderAdapter/         # NativeAOT C# adapter project
│   ├── RawReaderAdapter.cs   # [UnmanagedCallersOnly] export functions
│   └── RawReaderAdapter.csproj
└── src/
    └── lib.rs                # Safe Rust wrapper around the C ABI

```

---

## Building the Shared Library

To compile `librawfilereader` for your operating system and architecture, use `dotnet publish` with NativeAOT:

### macOS
```bash
# Apple Silicon (M1/M2/M3/M4)
dotnet publish RawReaderAdapter/RawReaderAdapter.csproj -c Release -r osx-arm64

# Intel x86_64
dotnet publish RawReaderAdapter/RawReaderAdapter.csproj -c Release -r osx-x64
```

### Linux

#### 1. Prerequisites
On Linux, NativeAOT requires the standard C build toolchain, `clang`, and `zlib`:

- **Ubuntu / Debian:**
  ```bash
  sudo apt-get update
  sudo apt-get install -y dotnet-sdk-8.0 clang zlib1g-dev build-essential
  ```
- **RHEL / Fedora / Rocky:**
  ```bash
  sudo dnf install -y dotnet-sdk-8.0 clang zlib-devel gcc
  ```
- **Arch Linux:**
  ```bash
  sudo pacman -S dotnet-sdk clang zlib base-devel
  ```

#### 2. Native Build
```bash
# Linux x86_64
dotnet publish RawReaderAdapter/RawReaderAdapter.csproj -c Release -r linux-x64

# Linux ARM64 (e.g. AWS Graviton, Raspberry Pi 4/5)
dotnet publish RawReaderAdapter/RawReaderAdapter.csproj -c Release -r linux-arm64
```
The output `librawfilereader.so` is automatically copied into the `lib/` directory.

#### 3. Building for Linux via Docker (from macOS or Windows)
If you are developing on macOS or Windows and want to produce `librawfilereader.so` without a Linux VM:
```bash
docker run --rm -v "$PWD":/src -w /src/RawReaderAdapter \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  sh -c "apt-get update && apt-get install -y clang zlib1g-dev && dotnet publish -c Release -r linux-x64"
```

### Windows
```cmd
:: Windows x64
dotnet publish RawReaderAdapter/RawReaderAdapter.csproj -c Release -r win-x64
```

> **Note:** The build target automatically copies the generated shared library (`.dylib`, `.so`, or `.dll`) into the `lib/` directory and configures macOS `@rpath` so it can be loaded relative to your executable.

---

## C ABI Header (`rawfilereader.h`)

All native functions follow standard C calling conventions (`cdecl` on x86, standard C on x64/ARM64). Every function returning `int` returns `RAW_SUCCESS` (`0`) on success or a non-zero error code.

### Core Data Structures

```c
typedef void* RawFileHandle;

typedef struct RawSpectrumData {
    double*  masses;          /* Pointer to m/z array */
    double*  intensities;     /* Pointer to intensity array */
    int32_t  count;           /* Number of data points */
} RawSpectrumData;

typedef struct RawScanStats {
    int32_t scan_number;
    int32_t ms_level;         /* 1 = MS1, 2 = MS2, 3 = MS3... */
    int32_t polarity;         /* 1 = Positive, 2 = Negative */
    int32_t is_centroid;      /* 1 = Centroid, 0 = Profile */
    int32_t packet_count;
    double  retention_time;   /* Minutes */
    double  tic;              /* Total Ion Current */
    double  base_peak_mass;
    double  base_peak_intensity;
    double  low_mass;
    double  high_mass;
} RawScanStats;

typedef struct RawPrecursorInfo {
    int32_t master_scan;      /* Precursor scan number (0 if unavailable) */
    double  precursor_mass;   /* Isolation target m/z */
    double  isolation_width;  /* Isolation window width */
    int32_t charge_state;     /* Precursor charge (0 if unknown) */
    double  monoisotopic_mass;/* Monoisotopic m/z */
    double  collision_energy; /* Collision energy in eV */
} RawPrecursorInfo;
```

---

## Using Outside Rust

### 1. C / C++

Include [`include/rawfilereader.h`](include/rawfilereader.h) and link against `librawfilereader`.

#### Minimal Example (`main.c`):
```c
#include <stdio.h>
#include "rawfilereader.h"

int main(void) {
    RawFileHandle handle = NULL;
    int rc = raw_open_file("sample.raw", &handle);
    if (rc != RAW_SUCCESS) {
        fprintf(stderr, "Failed to open RAW file: error code %d\n", rc);
        return 1;
    }

    int32_t first_scan = 0, last_scan = 0;
    raw_get_first_scan(handle, &first_scan);
    raw_get_last_scan(handle, &last_scan);
    printf("Scans: %d to %d\n", first_scan, last_scan);

    // Read scan 1 statistics
    RawScanStats stats;
    if (raw_get_scan_stats(handle, 1, &stats) == RAW_SUCCESS) {
        printf("Scan 1: MS%d, RT = %.3f min, TIC = %.2e\n",
               stats.ms_level, stats.retention_time, stats.tic);
    }

    // Read best spectrum (centroid stream if available, fallback to profile)
    RawSpectrumData spec;
    int32_t is_centroid = 0;
    if (raw_get_best_spectrum(handle, 1, &spec, &is_centroid) == RAW_SUCCESS) {
        printf("Scan 1 spectrum: %d peaks (%s)\n",
               spec.count, is_centroid ? "Centroid" : "Profile");
        if (spec.count > 0) {
            printf("  First peak: m/z %.4f, int %.1f\n", spec.masses[0], spec.intensities[0]);
        }
        raw_free_spectrum_data(&spec); // Always free spectrum data
    }

    raw_close_file(handle);
    return 0;
}
```

#### Compilation Commands:

- **macOS (Clang):**
  ```bash
  clang -Iinclude -Llib -lrawfilereader -Wl,-rpath,@loader_path/../lib -o main main.c
  ```
- **Linux (GCC / Clang):**
  ```bash
  gcc -Iinclude -Llib -lrawfilereader -Wl,-rpath,'$ORIGIN/../lib' -o main main.c
  ```
- **Windows (MSVC):**
  ```cmd
  cl /Iinclude main.c /link /LIBPATH:lib rawfilereader.lib
  ```

---

### 2. Python (Zero External Dependencies)

Use Python's built-in `ctypes` module to call the library directly without requiring any third-party packages.

A ready-to-run example is provided in [`examples/python_example.py`](examples/python_example.py):
```bash
python3 examples/python_example.py path/to/sample.raw
```

#### Minimal Python Snippet:
```python
import ctypes
from ctypes import c_void_p, c_char_p, c_int32, c_double, POINTER, Structure, byref

# Define RawSpectrumData struct
class RawSpectrumData(Structure):
    _fields_ = [
        ("masses", POINTER(c_double)),
        ("intensities", POINTER(c_double)),
        ("count", c_int32),
    ]

# Load library
lib = ctypes.CDLL("lib/librawfilereader.dylib") # or .so / .dll

# Declare function signatures
lib.raw_open_file.argtypes = [c_char_p, POINTER(c_void_p)]
lib.raw_open_file.restype = c_int32
lib.raw_close_file.argtypes = [c_void_p]
lib.raw_close_file.restype = None
lib.raw_get_best_spectrum.argtypes = [c_void_p, c_int32, POINTER(RawSpectrumData), POINTER(c_int32)]
lib.raw_get_best_spectrum.restype = c_int32
lib.raw_free_spectrum_data.argtypes = [POINTER(RawSpectrumData)]
lib.raw_free_spectrum_data.restype = None

# Open and read
handle = c_void_p()
if lib.raw_open_file(b"sample.raw", byref(handle)) == 0:
    spec = RawSpectrumData()
    is_centroid = c_int32()
    if lib.raw_get_best_spectrum(handle, 1, byref(spec), byref(is_centroid)) == 0:
        masses = [spec.masses[i] for i in range(spec.count)]
        intensities = [spec.intensities[i] for i in range(spec.count)]
        print(f"Read {len(masses)} peaks")
        lib.raw_free_spectrum_data(byref(spec))
    lib.raw_close_file(handle)
```

> **NumPy Zero-Copy Tip:** If NumPy is available, you can wrap the pointer without copying:
> ```python
> import numpy as np
> masses = np.ctypeslib.as_array(spec.masses, shape=(spec.count,)).copy()
> intensities = np.ctypeslib.as_array(spec.intensities, shape=(spec.count,)).copy()
> lib.raw_free_spectrum_data(byref(spec))
> ```

---

### 3. Haskell

The native C ABI can be consumed cleanly in Haskell using standard GHC FFI (zero external Cabal dependencies).

An idiomatic example demonstrating extensive use of `Either` (for operations that can fail) and `Maybe` (for optional metadata, queries, and monadic combinators) is provided in [`examples/haskell_example.hs`](examples/haskell_example.hs).

#### Compilation:
```bash
# macOS
ghc -O2 -Llib -lrawfilereader -optl-Wl,-rpath,@loader_path/../lib \
    examples/haskell_example.hs -o examples/haskell_example

# Linux
ghc -O2 -Llib -lrawfilereader -optl-Wl,-rpath,'$ORIGIN/../lib' \
    examples/haskell_example.hs -o examples/haskell_example

# Run
./examples/haskell_example path/to/sample.raw
```

#### Core Haskell Types & Combinators:
```haskell
-- Fallible operations return 'Either RawError a'
withRawFile       :: FilePath -> (RawFileHandle -> IO (Either RawError a)) -> IO (Either RawError a)
getScanStats      :: RawFileHandle -> Int -> IO (Either RawError RawScanStats)
getSpectrum       :: RawFileHandle -> Int -> IO (Either RawError Spectrum)

-- Optional data and queries return 'Maybe a'
lookupPrecursorInfo :: RawFileHandle -> Int -> IO (Maybe RawPrecursorInfo)
lookupTrailerExtra  :: RawFileHandle -> Int -> String -> IO (Maybe String)
findFirstScanOfLevel:: RawFileHandle -> Int -> Int -> Int -> IO (Maybe Int)

-- Monadic and parsing helpers
parseSpsMasses    :: Maybe String -> Maybe [Double]
polarityFromInt   :: CInt -> Maybe Polarity
optionalCharge    :: CInt -> Maybe Int
```

---

### 4. C# / .NET (P/Invoke)

If you are calling `librawfilereader` from a separate C#/.NET application:

```csharp
using System;
using System.Runtime.InteropServices;

public static class RawFileReaderNative
{
    private const string LibName = "librawfilereader";

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int raw_open_file(string filePath, out IntPtr handle);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void raw_close_file(IntPtr handle);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int raw_get_spectra_count(IntPtr handle, out int count);
}

// Usage:
if (RawFileReaderNative.raw_open_file("sample.raw", out IntPtr handle) == 0)
{
    RawFileReaderNative.raw_get_spectra_count(handle, out int count);
    Console.WriteLine($"Total spectra: {count}");
    RawFileReaderNative.raw_close_file(handle);
}
```

---

### 5. Julia (`ccall`)

In Julia, call functions directly using `ccall`:

```julia
const LIBRAW = "lib/librawfilereader.dylib" # or .so

# Open file
handle = Ref{Ptr{Cvoid}}(C_NULL)
rc = ccall((:raw_open_file, LIBRAW), Cint, (Cstring, Ptr{Ptr{Cvoid}}), "sample.raw", handle)
if rc == 0
    first_scan = Ref{Int32}(0)
    last_scan = Ref{Int32}(0)
    ccall((:raw_get_first_scan, LIBRAW), Cint, (Ptr{Cvoid}, Ptr{Int32}), handle[], first_scan)
    ccall((:raw_get_last_scan, LIBRAW), Cint, (Ptr{Cvoid}, Ptr{Int32}), handle[], last_scan)
    println("Scans: $(first_scan[]) to $(last_scan[])")

    # Close file
    ccall((:raw_close_file, LIBRAW), Cvoid, (Ptr{Cvoid},), handle[])
end
```

---

### 6. R (`dyn.load`)

In R, load the shared library and use `.C` or `foreign::`:

```r
dyn.load("lib/librawfilereader.so") # or .dylib / .dll

# raw_open_file
res <- .C("raw_open_file",
          as.character("sample.raw"),
          handle = raw(8),
          PACKAGE = "librawfilereader")
```

---

## Memory Management Rules

The native library allocates dynamic memory for strings, spectrum arrays, and chromatogram arrays using its internal allocator. To prevent memory leaks, you must follow the **allocator-frees rule**:

| Allocated Type | Returned By | Free Function |
|---|---|---|
| `char**` (strings) | `raw_get_instrument_model`, `raw_get_scan_filter`, `raw_get_trailer_extra_value_for_label`, etc. | `raw_free_string(char* str)` |
| `RawSpectrumData` | `raw_get_best_spectrum`, `raw_get_segmented_scan`, `raw_get_centroid_stream`, `raw_get_average_spectrum` | `raw_free_spectrum_data(RawSpectrumData* data)` |
| `RawChromatogramData` | `raw_get_chromatogram` | `raw_free_chromatogram_data(RawChromatogramData* data)` |
| `RawFileHandle` | `raw_open_file` | `raw_close_file(RawFileHandle handle)` |

---

## Thread Safety & Parallel Processing

1. **Multiple Files in Parallel:**
   - Multiple threads or processes can each open their own `RawFileHandle` independently without any locks or contention.
2. **Parallel Scan Extraction from a Single File:**
   - Concurrent calls to `raw_get_scan_stats`, `raw_get_best_spectrum`, `raw_get_precursor_info`, etc. on different scan numbers are thread-safe.
   - You can safely use thread pools (such as OpenMP in C/C++, `concurrent.futures` in Python, or Rayon in Rust) to process spectra concurrently.

---

## Error Codes

Defined in `include/rawfilereader.h`:

| Code | Enumeration | Description |
|---|---|---|
| `0` | `RAW_SUCCESS` | Operation succeeded |
| `101` | `RAW_ERROR_FILE_NAME_EMPTY` | File path was empty or null |
| `102` | `RAW_ERROR_FILE_NOT_FOUND` | File does not exist |
| `103` | `RAW_ERROR_FILE_OPEN_FAILED` | File cannot be opened (bad permissions, locked, etc.) |
| `104` | `RAW_ERROR_FILE_CORRUPT` | File is corrupted or not a valid Thermo RAW file |
| `201` | `RAW_ERROR_NULL_POINTER` | Null pointer passed for required output parameter |
| `202` | `RAW_ERROR_INVALID_HANDLE` | Provided `RawFileHandle` is null or closed |
| `301` | `RAW_ERROR_SCAN_OUT_OF_RANGE` | Requested scan number is outside `[first_scan, last_scan]` |
| `302` | `RAW_ERROR_NO_DATA` | Scan exists but has no data |
| `303` | `RAW_ERROR_NO_CENTROID_STREAM` | Scan does not contain a centroid stream |
| `304` | `RAW_ERROR_NO_PRECURSOR_INFO` | Scan is not an MSn scan or has no precursor data |
| `401` | `RAW_ERROR_OUT_OF_MEMORY` | Memory allocation failed |
