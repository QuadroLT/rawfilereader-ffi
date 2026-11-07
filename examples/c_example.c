#include <stdio.h>
#include <stdlib.h>
#include "rawfilereader.h"

int main(int argc, char* argv[]) {
    printf("=== ThermoRawFileReader C ABI Translation Layer Test ===\n\n");

    const char* file_path = argc > 1 ? argv[1] : "examples/dda_tmt_example.raw";
    printf("Opening file: %s\n", file_path);

    RawFileHandle handle = NULL;
    int err = raw_open_file(file_path, &handle);

    if (err != RAW_SUCCESS) {
        printf("raw_open_file returned error code: %d\n", err);
        return 1;
    }

    int32_t first_scan = 0, last_scan = 0, count = 0;
    double start_time = 0.0, end_time = 0.0;

    raw_get_first_scan(handle, &first_scan);
    raw_get_last_scan(handle, &last_scan);
    raw_get_spectra_count(handle, &count);
    raw_get_start_time(handle, &start_time);
    raw_get_end_time(handle, &end_time);

    printf("File Info:\n");
    printf("  Scans:       %d - %d (Total: %d)\n", first_scan, last_scan, count);
    printf("  Time Range:  %.2f - %.2f min\n", start_time, end_time);

    char* model = NULL;
    if (raw_get_instrument_model(handle, &model) == RAW_SUCCESS && model != NULL) {
        printf("  Instrument:  %s\n", model);
        raw_free_string(model);
    }

    // Survey scan levels
    int ms1_count = 0, ms2_count = 0, ms3_count = 0;
    int first_ms2 = 0, first_ms3 = 0, eluting_ms3 = 0;

    for (int s = first_scan; s <= last_scan; s++) {
        RawScanStats s_stats;
        if (raw_get_scan_stats(handle, s, &s_stats) == RAW_SUCCESS) {
            if (s_stats.ms_level == 1) ms1_count++;
            else if (s_stats.ms_level == 2) {
                ms2_count++;
                if (first_ms2 == 0) first_ms2 = s;
            } else if (s_stats.ms_level == 3) {
                ms3_count++;
                if (first_ms3 == 0) first_ms3 = s;
                if (eluting_ms3 == 0 && s_stats.retention_time >= 8.0) {
                    eluting_ms3 = s;
                }
            }
        }
    }

    printf("\nScan Breakdown:\n");
    printf("  MS1 scans:   %d\n", ms1_count);
    printf("  MS2 scans:   %d\n", ms2_count);
    printf("  MS3 scans:   %d\n", ms3_count);

    // 1. Inspect MS1 Scan (Scan 1)
    if (first_scan > 0) {
        printf("\n--- MS1 Scan (Scan %d) ---\n", first_scan);
        RawScanStats stats;
        raw_get_scan_stats(handle, first_scan, &stats);
        printf("  RT:        %.3f min\n", stats.retention_time);
        printf("  TIC:       %.2e\n", stats.tic);
        printf("  Base Peak: m/z %.4f (int %.2e)\n", stats.base_peak_mass, stats.base_peak_intensity);

        char* filter = NULL;
        if (raw_get_scan_filter(handle, first_scan, &filter) == RAW_SUCCESS && filter != NULL) {
            printf("  Filter:    %s\n", filter);
            raw_free_string(filter);
        }

        RawSpectrumData spectrum;
        int32_t is_centroid = 0;
        if (raw_get_best_spectrum(handle, first_scan, &spectrum, &is_centroid) == RAW_SUCCESS) {
            printf("  Spectrum:  %d peaks (%s)\n", spectrum.count, is_centroid ? "Centroid" : "Profile");
            raw_free_spectrum_data(&spectrum);
        }
    }

    // 2. Inspect MS2 Scan
    if (first_ms2 > 0) {
        printf("\n--- MS2 Scan (Scan %d) ---\n", first_ms2);
        RawScanStats stats;
        raw_get_scan_stats(handle, first_ms2, &stats);
        printf("  RT:        %.3f min\n", stats.retention_time);
        printf("  TIC:       %.2e\n", stats.tic);

        char* filter = NULL;
        if (raw_get_scan_filter(handle, first_ms2, &filter) == RAW_SUCCESS && filter != NULL) {
            printf("  Filter:    %s\n", filter);
            raw_free_string(filter);
        }

        RawPrecursorInfo prec;
        if (raw_get_precursor_info(handle, first_ms2, &prec) == RAW_SUCCESS) {
            printf("  Precursor m/z:   %.4f\n", prec.precursor_mass);
            printf("  Isolation width: %.2f\n", prec.isolation_width);
            printf("  Charge:          %d\n", prec.charge_state);
            printf("  Master MS1 scan: %d\n", prec.master_scan);
        }

        RawSpectrumData spec;
        int32_t is_centroid = 0;
        if (raw_get_best_spectrum(handle, first_ms2, &spec, &is_centroid) == RAW_SUCCESS) {
            printf("  Spectrum:  %d peaks (%s)\n", spec.count, is_centroid ? "Centroid" : "Profile");
            raw_free_spectrum_data(&spec);
        }
    }

    // 3. Inspect MS3 Scan (TMT SPS-MS3)
    int ms3_to_inspect = eluting_ms3 > 0 ? eluting_ms3 : first_ms3;
    if (ms3_to_inspect > 0) {
        printf("\n--- MS3 Scan (Scan %d, SPS-MS3 Quantification) ---\n", ms3_to_inspect);
        RawScanStats stats;
        raw_get_scan_stats(handle, ms3_to_inspect, &stats);
        printf("  RT:        %.3f min\n", stats.retention_time);
        printf("  TIC:       %.2e\n", stats.tic);
        printf("  Base Peak: m/z %.4f (int %.2e)\n", stats.base_peak_mass, stats.base_peak_intensity);

        char* filter = NULL;
        if (raw_get_scan_filter(handle, ms3_to_inspect, &filter) == RAW_SUCCESS && filter != NULL) {
            printf("  Filter:    %s\n", filter);
            raw_free_string(filter);
        }

        RawPrecursorInfo prec;
        if (raw_get_precursor_info(handle, ms3_to_inspect, &prec) == RAW_SUCCESS) {
            printf("  Precursor m/z:   %.4f\n", prec.precursor_mass);
            printf("  Isolation width: %.2f\n", prec.isolation_width);
            printf("  Master MS2 scan: %d\n", prec.master_scan);
        }

        char* sps = NULL;
        if (raw_get_trailer_extra_value_for_label(handle, ms3_to_inspect, "SPS Masses", &sps) == RAW_SUCCESS && sps != NULL) {
            printf("  SPS Notches:     %s\n", sps);
            raw_free_string(sps);
        }

        RawSpectrumData spec;
        int32_t is_centroid = 0;
        if (raw_get_best_spectrum(handle, ms3_to_inspect, &spec, &is_centroid) == RAW_SUCCESS) {
            printf("  Spectrum:  %d peaks (%s)\n", spec.count, is_centroid ? "Centroid" : "Profile");
            printf("\n  TMT Reporter Ions (126.0 - 135.0 m/z):\n");
            int reporters = 0;
            for (int i = 0; i < spec.count; i++) {
                if (spec.masses[i] >= 126.0 && spec.masses[i] <= 135.5) {
                    printf("    m/z %8.4f : intensity %10.1f\n", spec.masses[i], spec.intensities[i]);
                    reporters++;
                }
            }
            if (reporters == 0) printf("    (No reporter peaks detected)\n");
            raw_free_spectrum_data(&spec);
        }
    }

    raw_close_file(handle);
    printf("\nFile closed cleanly.\n");
    return 0;
}
