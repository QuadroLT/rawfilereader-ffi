#ifndef RAWFILEREADER_H
#define RAWFILEREADER_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>
#include <stdbool.h>

/**
 * ============================================================================
 * Error Codes
 * ============================================================================
 * All functions returning an int return RAW_SUCCESS (0) on success,
 * or one of the error codes below.
 * In languages like Rust, these map directly to Result<T, RawError> and Option<T>.
 */
typedef enum RawErrorCode {
    RAW_SUCCESS                     = 0,

    // File errors (100 - 199)
    RAW_ERROR_FILE_NAME_EMPTY       = 101,
    RAW_ERROR_FILE_NOT_FOUND        = 102,
    RAW_ERROR_FILE_OPEN_FAILED      = 103,
    RAW_ERROR_FILE_CORRUPT          = 104,
    RAW_ERROR_FILE_UNDEFINED        = 199,

    // Handle & Pointer errors (200 - 299)
    RAW_ERROR_NULL_POINTER          = 201,
    RAW_ERROR_INVALID_HANDLE        = 202,

    // Scan & Data errors (300 - 399)
    RAW_ERROR_SCAN_OUT_OF_RANGE     = 301,
    RAW_ERROR_NO_DATA               = 302,
    RAW_ERROR_NO_CENTROID_STREAM    = 303,
    RAW_ERROR_NO_PRECURSOR_INFO     = 304,
    RAW_ERROR_NO_INSTRUMENT         = 305,

    // General errors (400 - 499)
    RAW_ERROR_OUT_OF_MEMORY         = 401,
    RAW_ERROR_BUFFER_TOO_SMALL      = 402,
    RAW_ERROR_UNEXPECTED            = 499
} RawErrorCode;

/**
 * Opaque pointer representing an open RAW file session.
 */
typedef void* RawFileHandle;

/**
 * ============================================================================
 * Data Structures
 * ============================================================================
 */

/**
 * Spectrum data containing parallel arrays of m/z and intensity.
 * Buffers are allocated by the library and must be freed with raw_free_spectrum_data.
 */
typedef struct RawSpectrumData {
    double* masses;         /* Pointer to array of m/z values */
    double* intensities;    /* Pointer to array of intensity values */
    int32_t count;          /* Number of data points */
} RawSpectrumData;

/**
 * Comprehensive scan statistics and metadata.
 */
typedef struct RawScanStats {
    int32_t scan_number;
    int32_t ms_level;             /* 1 = MS1, 2 = MS2, etc. */
    int32_t polarity;             /* 1 = Positive (+), 2 = Negative (-), 0 = Unknown */
    int32_t is_centroid;          /* 1 if centroid scan, 0 if profile */
    int32_t packet_count;         /* Number of packets */
    double  retention_time;       /* Retention time in minutes */
    double  tic;                  /* Total Ion Current */
    double  base_peak_mass;       /* m/z of the base peak */
    double  base_peak_intensity;  /* Intensity of the base peak */
    double  low_mass;             /* Lowest m/z in scan */
    double  high_mass;            /* Highest m/z in scan */
} RawScanStats;

/**
 * Precursor information for MSn scans (e.g. MS2).
 */
typedef struct RawPrecursorInfo {
    int32_t master_scan;          /* Scan number of precursor MS1 (0 if unavailable) */
    double  precursor_mass;       /* Isolation target m/z */
    double  isolation_width;      /* Isolation window width in m/z */
    int32_t charge_state;         /* Precursor charge state (0 if unknown) */
    double  monoisotopic_mass;    /* Monoisotopic m/z (0.0 if not determined) */
    double  collision_energy;     /* Collision energy in eV (0.0 if not available) */
} RawPrecursorInfo;

/**
 * Chromatogram data containing parallel arrays of retention time and intensity.
 * Buffers are allocated by the library and must be freed with raw_free_chromatogram_data.
 */
typedef struct RawChromatogramData {
    double* times;                /* Pointer to array of retention times in minutes */
    double* intensities;          /* Pointer to array of intensity values */
    int32_t count;                /* Number of data points */
} RawChromatogramData;

/**
 * Chromatogram trace types.
 */
typedef enum RawTraceType {
    RAW_TRACE_BASE_PEAK  = 0,
    RAW_TRACE_TIC        = 1,
    RAW_TRACE_MASS_RANGE = 2,
} RawTraceType;

/**
 * ============================================================================
 * Functions
 * ============================================================================
 */

/* --- File Lifecycle --- */

/**
 * Open a Thermo .RAW file.
 *
 * @param file_path UTF-8 null-terminated path to the .RAW file.
 * @param out_handle Pointer to store the resulting RawFileHandle.
 * @return RAW_SUCCESS on success, error code otherwise.
 */
int raw_open_file(const char* file_path, RawFileHandle* out_handle);

/**
 * Close an open RAW file and release all associated resources.
 *
 * @param handle The RawFileHandle previously returned by raw_open_file.
 */
void raw_close_file(RawFileHandle handle);

/* --- Run / File Information --- */

/**
 * Get the first scan number in the file (typically 1).
 */
int raw_get_first_scan(RawFileHandle handle, int32_t* out_first_scan);

/**
 * Get the last scan number in the file.
 */
int raw_get_last_scan(RawFileHandle handle, int32_t* out_last_scan);

/**
 * Get total number of spectra in the file.
 */
int raw_get_spectra_count(RawFileHandle handle, int32_t* out_count);

/**
 * Get run start time (in minutes).
 */
int raw_get_start_time(RawFileHandle handle, double* out_start_time);

/**
 * Get run end time (in minutes).
 */
int raw_get_end_time(RawFileHandle handle, double* out_end_time);

/**
 * Get overall mass range of the run.
 */
int raw_get_mass_range(RawFileHandle handle, double* out_low_mass, double* out_high_mass);

/**
 * Get instrument model string (e.g. "Q Exactive HF", "Orbitrap Exploris 480").
 * The returned string must be freed using raw_free_string.
 */
int raw_get_instrument_model(RawFileHandle handle, char** out_model_str);

/**
 * Get instrument name string.
 * The returned string must be freed using raw_free_string.
 */
int raw_get_instrument_name(RawFileHandle handle, char** out_name_str);

/**
 * Get instrument serial number.
 * The returned string must be freed using raw_free_string.
 */
int raw_get_instrument_serial_number(RawFileHandle handle, char** out_serial_number);

/**
 * Get instrument software version.
 * The returned string must be freed using raw_free_string.
 */
int raw_get_instrument_software_version(RawFileHandle handle, char** out_software_version);

/**
 * Get instrument hardware/firmware version.
 * The returned string must be freed using raw_free_string.
 */
int raw_get_instrument_hardware_version(RawFileHandle handle, char** out_hardware_version);

/**
 * Get mass resolution.
 */
int raw_get_mass_resolution(RawFileHandle handle, double* out_mass_resolution);

/**
 * Get total number of instruments present in the file.
 */
int raw_get_instrument_count(RawFileHandle handle, int32_t* out_count);

/**
 * Get total number of unique filters in the file.
 */
int raw_get_filter_count(RawFileHandle handle, int32_t* out_count);

/**
 * Get original file path / name.
 * The returned string must be freed using raw_free_string.
 */
int raw_get_file_name(RawFileHandle handle, char** out_file_name);

/**
 * Get file creation date (ISO 8601 string).
 * The returned string must be freed using raw_free_string.
 */
int raw_get_creation_date(RawFileHandle handle, char** out_creation_date);

/**
 * Get file description string.
 * The returned string must be freed using raw_free_string.
 */
int raw_get_file_description(RawFileHandle handle, char** out_description);

/* --- Sample Information --- */

/**
 * Get sample name.
 * The returned string must be freed using raw_free_string.
 */
int raw_get_sample_name(RawFileHandle handle, char** out_name);

/**
 * Get sample ID.
 * The returned string must be freed using raw_free_string.
 */
int raw_get_sample_id(RawFileHandle handle, char** out_id);

/**
 * Get sample comment.
 * The returned string must be freed using raw_free_string.
 */
int raw_get_sample_comment(RawFileHandle handle, char** out_comment);

/**
 * Get sample vial.
 * The returned string must be freed using raw_free_string.
 */
int raw_get_sample_vial(RawFileHandle handle, char** out_vial);

/* --- Scan Metadata --- */

/**
 * Get retention time in minutes for a specific scan.
 */
int raw_get_scan_retention_time(RawFileHandle handle, int32_t scan_number, double* out_rt);

/**
 * Find the scan number closest to the given retention time (in minutes).
 */
int raw_get_scan_number_from_retention_time(RawFileHandle handle, double retention_time, int32_t* out_scan);

/**
 * Get full scan statistics (MS level, polarity, TIC, base peak, etc.).
 */
int raw_get_scan_stats(RawFileHandle handle, int32_t scan_number, RawScanStats* out_stats);

/**
 * Get the scan filter string (e.g., "FTMS + p NSI Full ms [350.00-1800.00]").
 * The returned string must be freed using raw_free_string.
 */
int raw_get_scan_filter(RawFileHandle handle, int32_t scan_number, char** out_filter_str);

/**
 * Get precursor info for MSn scans (master scan, isolation m/z, charge, etc.).
 * Returns RAW_ERROR_NO_PRECURSOR_INFO if the scan has no precursor data.
 */
int raw_get_precursor_info(RawFileHandle handle, int32_t scan_number, RawPrecursorInfo* out_info);

/* --- Trailer Extra Metadata --- */

/**
 * Get the count of Trailer Extra items for a given scan.
 */
int raw_get_trailer_extra_count(RawFileHandle handle, int32_t scan_number, int32_t* out_count);

/**
 * Get a specific Trailer Extra entry by index.
 * Both out_label and out_value are allocated by the library and must be freed with raw_free_string.
 */
int raw_get_trailer_extra_item(RawFileHandle handle, int32_t scan_number, int32_t index, char** out_label, char** out_value);

/**
 * Find a Trailer Extra value by label prefix (e.g., "Master Scan", "Charge State", "Monoisotopic M/Z", "HCD Energy").
 * The returned string must be freed with raw_free_string.
 */
int raw_get_trailer_extra_value_for_label(RawFileHandle handle, int32_t scan_number, const char* label, char** out_value);

/* --- Spectrum Data --- */

/**
 * Get segmented scan (raw profile or centroid data as stored by instrument).
 * Allocates masses and intensities buffers inside out_data.
 * Must be freed using raw_free_spectrum_data.
 */
int raw_get_segmented_scan(RawFileHandle handle, int32_t scan_number, RawSpectrumData* out_data);

/**
 * Get centroid stream (centroided peaks for FTMS/Orbitrap scans).
 * Returns RAW_ERROR_NO_CENTROID_STREAM if no centroid stream is present.
 * Must be freed using raw_free_spectrum_data.
 */
int raw_get_centroid_stream(RawFileHandle handle, int32_t scan_number, RawSpectrumData* out_data);

/**
 * Get spectrum data, preferring centroid stream if available, otherwise falling back
 * to segmented scan.
 * Sets out_is_centroid to 1 if centroids were obtained, 0 if profile.
 * Must be freed using raw_free_spectrum_data.
 */
int raw_get_best_spectrum(RawFileHandle handle, int32_t scan_number, RawSpectrumData* out_data, int32_t* out_is_centroid);

/**
 * Get an averaged spectrum across a scan range.
 * Allocates masses and intensities buffers inside out_data.
 * Sets out_is_centroid to 1 if centroids were obtained, 0 if profile.
 * Must be freed using raw_free_spectrum_data.
 */
int raw_get_average_spectrum(RawFileHandle handle, int32_t first_scan, int32_t last_scan, RawSpectrumData* out_data, int32_t* out_is_centroid);

/* --- Chromatograms --- */

/**
 * Get chromatogram trace (BasePeak = 0, TIC = 1, MassRange = 2).
 * If start_scan or end_scan is <= 0, defaults to whole file range.
 * Filter can be NULL for unfiltered trace.
 * Buffers must be freed using raw_free_chromatogram_data.
 */
int raw_get_chromatogram(RawFileHandle handle, int32_t trace_type, int32_t start_scan, int32_t end_scan, const char* filter, RawChromatogramData* out_data);

/* --- Memory Cleanup --- */

/**
 * Free a string previously allocated by the library.
 */
void raw_free_string(char* str);

/**
 * Free spectrum buffers previously allocated by the library.
 */
void raw_free_spectrum_data(RawSpectrumData* data);

/**
 * Free chromatogram buffers previously allocated by the library.
 */
void raw_free_chromatogram_data(RawChromatogramData* data);

#ifdef __cplusplus
}
#endif

#endif /* RAWFILEREADER_H */
