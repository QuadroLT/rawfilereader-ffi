use std::ffi::{c_char, c_double, c_int, c_void, CStr, CString};
use std::ptr;

// Opaque C handle
pub type RawFileHandle = *mut c_void;


#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RawErrorCode {
    Success = 0,
    FileNameEmpty = 101,
    FileNotFound = 102,
    FileOpenFailed = 103,
    FileCorrupt = 104,
    FileUndefined = 199,
    NullPointer = 201,
    InvalidHandle = 202,
    ScanOutOfRange = 301,
    NoData = 302,
    NoCentroidStream = 303,
    NoPrecursorInfo = 304,
    NoInstrument = 305,
    OutOfMemory = 401,
    BufferTooSmall = 402,
    Unexpected = 499,
}

impl RawErrorCode {
    pub fn from_code(code: c_int) -> Self {
        match code {
            0 => RawErrorCode::Success,
            101 => RawErrorCode::FileNameEmpty,
            102 => RawErrorCode::FileNotFound,
            103 => RawErrorCode::FileOpenFailed,
            104 => RawErrorCode::FileCorrupt,
            199 => RawErrorCode::FileUndefined,
            201 => RawErrorCode::NullPointer,
            202 => RawErrorCode::InvalidHandle,
            301 => RawErrorCode::ScanOutOfRange,
            302 => RawErrorCode::NoData,
            303 => RawErrorCode::NoCentroidStream,
            304 => RawErrorCode::NoPrecursorInfo,
            305 => RawErrorCode::NoInstrument,
            401 => RawErrorCode::OutOfMemory,
            402 => RawErrorCode::BufferTooSmall,
            _ => RawErrorCode::Unexpected,
        }
    }
}

impl std::fmt::Display for RawErrorCode {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{self:?}")
    }
}

impl std::error::Error for RawErrorCode {}

pub type Result<T> = std::result::Result<T, RawErrorCode>;

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct RawScanStats {
    pub scan_number: c_int,
    pub ms_level: c_int,
    pub polarity: c_int,
    pub is_centroid: c_int,
    pub packet_count: c_int,
    pub retention_time: c_double,
    pub tic: c_double,
    pub base_peak_mass: c_double,
    pub base_peak_intensity: c_double,
    pub low_mass: c_double,
    pub high_mass: c_double,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct RawPrecursorInfo {
    pub master_scan: c_int,
    pub precursor_mass: c_double,
    pub isolation_width: c_double,
    pub charge_state: c_int,
    pub monoisotopic_mass: c_double,
    pub collision_energy: c_double,
}

#[repr(i32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RawTraceType {
    BasePeak = 0,
    Tic = 1,
    MassRange = 2,
}

#[derive(Debug, Clone, Default)]
pub struct RawSampleInfo {
    pub name: Option<String>,
    pub id: Option<String>,
    pub comment: Option<String>,
    pub vial: Option<String>,
}

#[repr(C)]
struct CRawSpectrumData {
    masses: *mut c_double,
    intensities: *mut c_double,
    count: c_int,
}

#[repr(C)]
struct CRawChromatogramData {
    times: *mut c_double,
    intensities: *mut c_double,
    count: c_int,
}

unsafe extern "C" {
    fn raw_open_file(file_path: *const c_char, out_handle: *mut RawFileHandle) -> c_int;
    fn raw_close_file(handle: RawFileHandle);
    fn raw_get_first_scan(handle: RawFileHandle, out_first_scan: *mut c_int) -> c_int;
    fn raw_get_last_scan(handle: RawFileHandle, out_last_scan: *mut c_int) -> c_int;
    fn raw_get_spectra_count(handle: RawFileHandle, out_count: *mut c_int) -> c_int;
    fn raw_get_start_time(handle: RawFileHandle, out_start_time: *mut c_double) -> c_int;
    fn raw_get_end_time(handle: RawFileHandle, out_end_time: *mut c_double) -> c_int;
    fn raw_get_mass_range(handle: RawFileHandle, out_low_mass: *mut c_double, out_high_mass: *mut c_double) -> c_int;
    fn raw_get_instrument_model(handle: RawFileHandle, out_model_str: *mut *mut c_char) -> c_int;
    fn raw_get_instrument_name(handle: RawFileHandle, out_name_str: *mut *mut c_char) -> c_int;
    fn raw_get_instrument_serial_number(handle: RawFileHandle, out_serial_str: *mut *mut c_char) -> c_int;
    fn raw_get_instrument_software_version(handle: RawFileHandle, out_version_str: *mut *mut c_char) -> c_int;
    fn raw_get_instrument_hardware_version(handle: RawFileHandle, out_version_str: *mut *mut c_char) -> c_int;
    fn raw_get_mass_resolution(handle: RawFileHandle, out_resolution: *mut c_double) -> c_int;
    fn raw_get_instrument_count(handle: RawFileHandle, out_count: *mut c_int) -> c_int;
    fn raw_get_filter_count(handle: RawFileHandle, out_count: *mut c_int) -> c_int;
    fn raw_get_file_name(handle: RawFileHandle, out_file_name_str: *mut *mut c_char) -> c_int;
    fn raw_get_creation_date(handle: RawFileHandle, out_date_str: *mut *mut c_char) -> c_int;
    fn raw_get_file_description(handle: RawFileHandle, out_desc_str: *mut *mut c_char) -> c_int;
    fn raw_get_sample_name(handle: RawFileHandle, out_str: *mut *mut c_char) -> c_int;
    fn raw_get_sample_id(handle: RawFileHandle, out_str: *mut *mut c_char) -> c_int;
    fn raw_get_sample_comment(handle: RawFileHandle, out_str: *mut *mut c_char) -> c_int;
    fn raw_get_sample_vial(handle: RawFileHandle, out_str: *mut *mut c_char) -> c_int;
    fn raw_get_scan_retention_time(handle: RawFileHandle, scan_number: c_int, out_rt: *mut c_double) -> c_int;
    fn raw_get_scan_number_from_retention_time(handle: RawFileHandle, retention_time: c_double, out_scan: *mut c_int) -> c_int;
    fn raw_get_scan_stats(handle: RawFileHandle, scan_number: c_int, out_stats: *mut RawScanStats) -> c_int;
    fn raw_get_scan_filter(handle: RawFileHandle, scan_number: c_int, out_filter_str: *mut *mut c_char) -> c_int;
    fn raw_get_precursor_info(handle: RawFileHandle, scan_number: c_int, out_info: *mut RawPrecursorInfo) -> c_int;
    fn raw_get_trailer_extra_count(handle: RawFileHandle, scan_number: c_int, out_count: *mut c_int) -> c_int;
    fn raw_get_trailer_extra_item(handle: RawFileHandle, scan_number: c_int, index: c_int, out_label: *mut *mut c_char, out_value: *mut *mut c_char) -> c_int;
    fn raw_get_trailer_extra_value_for_label(handle: RawFileHandle, scan_number: c_int, label: *const c_char, out_value: *mut *mut c_char) -> c_int;
    fn raw_get_segmented_scan(handle: RawFileHandle, scan_number: c_int, out_data: *mut CRawSpectrumData) -> c_int;
    fn raw_get_centroid_stream(handle: RawFileHandle, scan_number: c_int, out_data: *mut CRawSpectrumData) -> c_int;
    fn raw_get_best_spectrum(handle: RawFileHandle, scan_number: c_int, out_data: *mut CRawSpectrumData, out_is_centroid: *mut c_int) -> c_int;
    fn raw_get_average_spectrum(handle: RawFileHandle, first_scan: c_int, last_scan: c_int, out_data: *mut CRawSpectrumData, out_is_centroid: *mut c_int) -> c_int;
    fn raw_get_chromatogram(handle: RawFileHandle, trace_type: c_int, start_scan: c_int, end_scan: c_int, filter: *const c_char, out_data: *mut CRawChromatogramData) -> c_int;
    fn raw_free_string(str_ptr: *mut c_char);
    fn raw_free_spectrum_data(data: *mut CRawSpectrumData);
    fn raw_free_chromatogram_data(data: *mut CRawChromatogramData);
}

/// Owned spectrum data containing vectors of m/z and intensity
#[derive(Debug, Clone)]
pub struct RawSpectrum {
    pub masses: Vec<f64>,
    pub intensities: Vec<f64>,
    pub is_centroid: bool,
}

/// Owned chromatogram data containing vectors of retention times and intensities
#[derive(Debug, Clone)]
pub struct RawChromatogram {
    pub times: Vec<f64>,
    pub intensities: Vec<f64>,
}

fn c_spectrum_to_raw(c_data: &mut CRawSpectrumData, is_centroid: bool) -> RawSpectrum {
    let masses = if c_data.count > 0 && !c_data.masses.is_null() {
        unsafe { std::slice::from_raw_parts(c_data.masses, c_data.count as usize).to_vec() }
    } else {
        Vec::new()
    };

    let intensities = if c_data.count > 0 && !c_data.intensities.is_null() {
        unsafe { std::slice::from_raw_parts(c_data.intensities, c_data.count as usize).to_vec() }
    } else {
        Vec::new()
    };

    unsafe {
        raw_free_spectrum_data(c_data);
    }

    RawSpectrum {
        masses,
        intensities,
        is_centroid,
    }
}

/// RAII session for an open Thermo RAW file
#[derive(Debug)]
pub struct RawFileSession {
    handle: RawFileHandle,
}

impl RawFileSession {
    pub fn open(path: &str) -> Result<Self> {
        static INIT: std::sync::Once = std::sync::Once::new();
        INIT.call_once(|| {
            // One-time initialization of NativeAOT runtime to safely register signal handlers
            let dummy = CString::new("").unwrap();
            let mut h: RawFileHandle = ptr::null_mut();
            unsafe { raw_open_file(dummy.as_ptr(), &mut h); }
        });

        let c_path = CString::new(path).map_err(|_| RawErrorCode::FileNameEmpty)?;
        let mut handle: RawFileHandle = ptr::null_mut();

        let code = unsafe { raw_open_file(c_path.as_ptr(), &mut handle) };
        if code == 0 && !handle.is_null() {
            Ok(RawFileSession { handle })
        } else {
            Err(RawErrorCode::from_code(code))
        }
    }

    fn get_string_prop(
        &self,
        f: unsafe extern "C" fn(RawFileHandle, *mut *mut c_char) -> c_int,
    ) -> Result<String> {
        let mut ptr: *mut c_char = ptr::null_mut();
        let code = unsafe { f(self.handle, &mut ptr) };
        if code != 0 {
            return Err(RawErrorCode::from_code(code));
        }
        if ptr.is_null() {
            return Err(RawErrorCode::NoData);
        }
        let s = unsafe { CStr::from_ptr(ptr) }.to_string_lossy().into_owned();
        unsafe { raw_free_string(ptr); }
        if s.is_empty() {
            Err(RawErrorCode::NoData)
        } else {
            Ok(s)
        }
    }

    pub fn get_first_scan(&self) -> Result<i32> {
        let mut scan: c_int = 0;
        let code = unsafe { raw_get_first_scan(self.handle, &mut scan) };
        if code == 0 { Ok(scan) } else { Err(RawErrorCode::from_code(code)) }
    }

    pub fn get_last_scan(&self) -> Result<i32> {
        let mut scan: c_int = 0;
        let code = unsafe { raw_get_last_scan(self.handle, &mut scan) };
        if code == 0 { Ok(scan) } else { Err(RawErrorCode::from_code(code)) }
    }

    pub fn get_spectra_count(&self) -> Result<i32> {
        let mut count: c_int = 0;
        let code = unsafe { raw_get_spectra_count(self.handle, &mut count) };
        if code == 0 { Ok(count) } else { Err(RawErrorCode::from_code(code)) }
    }

    pub fn get_retention_time_range(&self) -> Result<(f64, f64)> {
        let mut start: c_double = 0.0;
        let mut end: c_double = 0.0;
        let c1 = unsafe { raw_get_start_time(self.handle, &mut start) };
        if c1 != 0 { return Err(RawErrorCode::from_code(c1)); }
        let c2 = unsafe { raw_get_end_time(self.handle, &mut end) };
        if c2 != 0 { return Err(RawErrorCode::from_code(c2)); }
        Ok((start, end))
    }

    pub fn get_mass_range(&self) -> Result<(f64, f64)> {
        let mut low: c_double = 0.0;
        let mut high: c_double = 0.0;
        let code = unsafe { raw_get_mass_range(self.handle, &mut low, &mut high) };
        if code == 0 { Ok((low, high)) } else { Err(RawErrorCode::from_code(code)) }
    }

    pub fn get_instrument_model(&self) -> Result<String> {
        self.get_string_prop(raw_get_instrument_model)
    }

    pub fn get_instrument_name(&self) -> Result<String> {
        self.get_string_prop(raw_get_instrument_name)
    }

    pub fn get_instrument_serial_number(&self) -> Result<String> {
        self.get_string_prop(raw_get_instrument_serial_number)
    }

    pub fn get_instrument_software_version(&self) -> Result<String> {
        self.get_string_prop(raw_get_instrument_software_version)
    }

    pub fn get_instrument_hardware_version(&self) -> Result<String> {
        self.get_string_prop(raw_get_instrument_hardware_version)
    }

    pub fn get_mass_resolution(&self) -> Result<f64> {
        let mut res: c_double = 0.0;
        let code = unsafe { raw_get_mass_resolution(self.handle, &mut res) };
        if code == 0 { Ok(res) } else { Err(RawErrorCode::from_code(code)) }
    }

    pub fn get_instrument_count(&self) -> Result<i32> {
        let mut count: c_int = 0;
        let code = unsafe { raw_get_instrument_count(self.handle, &mut count) };
        if code == 0 { Ok(count) } else { Err(RawErrorCode::from_code(code)) }
    }

    pub fn get_filter_count(&self) -> Result<i32> {
        let mut count: c_int = 0;
        let code = unsafe { raw_get_filter_count(self.handle, &mut count) };
        if code == 0 { Ok(count) } else { Err(RawErrorCode::from_code(code)) }
    }

    pub fn get_file_name(&self) -> Result<String> {
        self.get_string_prop(raw_get_file_name)
    }

    pub fn get_creation_date(&self) -> Result<String> {
        self.get_string_prop(raw_get_creation_date)
    }

    pub fn get_file_description(&self) -> Result<String> {
        self.get_string_prop(raw_get_file_description)
    }

    pub fn get_sample_name(&self) -> Result<String> {
        self.get_string_prop(raw_get_sample_name)
    }

    pub fn get_sample_id(&self) -> Result<String> {
        self.get_string_prop(raw_get_sample_id)
    }

    pub fn get_sample_comment(&self) -> Result<String> {
        self.get_string_prop(raw_get_sample_comment)
    }

    pub fn get_sample_vial(&self) -> Result<String> {
        self.get_string_prop(raw_get_sample_vial)
    }

    pub fn get_sample_info(&self) -> Result<RawSampleInfo> {
        let name = self.get_sample_name().ok();
        let id = self.get_sample_id().ok();
        let comment = self.get_sample_comment().ok();
        let vial = self.get_sample_vial().ok();
        Ok(RawSampleInfo { name, id, comment, vial })
    }

    pub fn get_scan_retention_time(&self, scan: i32) -> Result<f64> {
        let mut rt: c_double = 0.0;
        let code = unsafe { raw_get_scan_retention_time(self.handle, scan, &mut rt) };
        if code == 0 { Ok(rt) } else { Err(RawErrorCode::from_code(code)) }
    }

    pub fn get_scan_number_from_retention_time(&self, retention_time: f64) -> Result<i32> {
        let mut scan: c_int = 0;
        let code = unsafe { raw_get_scan_number_from_retention_time(self.handle, retention_time, &mut scan) };
        if code == 0 { Ok(scan) } else { Err(RawErrorCode::from_code(code)) }
    }

    pub fn get_scan_stats(&self, scan: i32) -> Result<RawScanStats> {
        let mut stats = RawScanStats {
            scan_number: 0,
            ms_level: 0,
            polarity: 0,
            is_centroid: 0,
            packet_count: 0,
            retention_time: 0.0,
            tic: 0.0,
            base_peak_mass: 0.0,
            base_peak_intensity: 0.0,
            low_mass: 0.0,
            high_mass: 0.0,
        };
        let code = unsafe { raw_get_scan_stats(self.handle, scan, &mut stats) };
        if code == 0 { Ok(stats) } else { Err(RawErrorCode::from_code(code)) }
    }

    pub fn get_scan_filter(&self, scan: i32) -> Result<String> {
        let mut ptr: *mut c_char = ptr::null_mut();
        let code = unsafe { raw_get_scan_filter(self.handle, scan, &mut ptr) };
        if code != 0 {
            return Err(RawErrorCode::from_code(code));
        }
        if ptr.is_null() {
            return Err(RawErrorCode::NoData);
        }
        let filter = unsafe { CStr::from_ptr(ptr) }.to_string_lossy().into_owned();
        unsafe { raw_free_string(ptr); }
        if filter.is_empty() {
            Err(RawErrorCode::NoData)
        } else {
            Ok(filter)
        }
    }

    /// Precursor information for MS2+ scans.
    /// Returns `Ok(RawPrecursorInfo)` on success, or `Err(RawErrorCode::NoPrecursorInfo)` if not an MSn scan.
    pub fn get_precursor_info(&self, scan: i32) -> Result<RawPrecursorInfo> {
        let mut info = RawPrecursorInfo {
            master_scan: 0,
            precursor_mass: 0.0,
            isolation_width: 0.0,
            charge_state: 0,
            monoisotopic_mass: 0.0,
            collision_energy: 0.0,
        };
        let code = unsafe { raw_get_precursor_info(self.handle, scan, &mut info) };
        if code == 0 {
            Ok(info)
        } else {
            Err(RawErrorCode::from_code(code))
        }
    }

    pub fn get_trailer_extra_count(&self, scan: i32) -> Result<i32> {
        let mut count: c_int = 0;
        let code = unsafe { raw_get_trailer_extra_count(self.handle, scan, &mut count) };
        if code == 0 { Ok(count) } else { Err(RawErrorCode::from_code(code)) }
    }

    pub fn get_trailer_extra_item(&self, scan: i32, index: i32) -> Result<(String, String)> {
        let mut lbl_ptr: *mut c_char = ptr::null_mut();
        let mut val_ptr: *mut c_char = ptr::null_mut();
        let code = unsafe { raw_get_trailer_extra_item(self.handle, scan, index, &mut lbl_ptr, &mut val_ptr) };
        if code != 0 {
            return Err(RawErrorCode::from_code(code));
        }
        let lbl = if !lbl_ptr.is_null() {
            let s = unsafe { CStr::from_ptr(lbl_ptr) }.to_string_lossy().into_owned();
            unsafe { raw_free_string(lbl_ptr); }
            s
        } else {
            String::new()
        };
        let val = if !val_ptr.is_null() {
            let s = unsafe { CStr::from_ptr(val_ptr) }.to_string_lossy().into_owned();
            unsafe { raw_free_string(val_ptr); }
            s
        } else {
            String::new()
        };
        Ok((lbl, val))
    }

    pub fn get_trailer_extra_value(&self, scan: i32, label: &str) -> Result<String> {
        let c_label = CString::new(label).map_err(|_| RawErrorCode::NullPointer)?;
        let mut val_ptr: *mut c_char = ptr::null_mut();
        let code = unsafe { raw_get_trailer_extra_value_for_label(self.handle, scan, c_label.as_ptr(), &mut val_ptr) };
        if code != 0 {
            return Err(RawErrorCode::from_code(code));
        }
        if val_ptr.is_null() {
            return Err(RawErrorCode::NoData);
        }
        let s = unsafe { CStr::from_ptr(val_ptr) }.to_string_lossy().into_owned();
        unsafe { raw_free_string(val_ptr); }
        Ok(s)
    }

    pub fn get_all_trailer_extra(&self, scan: i32) -> Result<Vec<(String, String)>> {
        let count = self.get_trailer_extra_count(scan)?;
        let mut items = Vec::with_capacity(count as usize);
        for i in 0..count {
            items.push(self.get_trailer_extra_item(scan, i)?);
        }
        Ok(items)
    }

    /// Retrieve segmented scan (raw profile or stored centroid data).
    pub fn get_segmented_scan(&self, scan: i32) -> Result<RawSpectrum> {
        let mut c_data = CRawSpectrumData {
            masses: ptr::null_mut(),
            intensities: ptr::null_mut(),
            count: 0,
        };
        let code = unsafe { raw_get_segmented_scan(self.handle, scan, &mut c_data) };
        if code != 0 {
            return Err(RawErrorCode::from_code(code));
        }
        Ok(c_spectrum_to_raw(&mut c_data, false))
    }

    /// Retrieve centroid stream (high-resolution FTMS/Orbitrap centroids).
    pub fn get_centroid_stream(&self, scan: i32) -> Result<RawSpectrum> {
        let mut c_data = CRawSpectrumData {
            masses: ptr::null_mut(),
            intensities: ptr::null_mut(),
            count: 0,
        };
        let code = unsafe { raw_get_centroid_stream(self.handle, scan, &mut c_data) };
        if code != 0 {
            return Err(RawErrorCode::from_code(code));
        }
        Ok(c_spectrum_to_raw(&mut c_data, true))
    }

    /// Retrieve best spectrum (preferring centroid stream, falling back to profile).
    pub fn get_spectrum(&self, scan: i32) -> Result<RawSpectrum> {
        let mut c_data = CRawSpectrumData {
            masses: ptr::null_mut(),
            intensities: ptr::null_mut(),
            count: 0,
        };
        let mut is_centroid: c_int = 0;

        let code = unsafe {
            raw_get_best_spectrum(self.handle, scan, &mut c_data, &mut is_centroid)
        };

        if code != 0 {
            return Err(RawErrorCode::from_code(code));
        }

        Ok(c_spectrum_to_raw(&mut c_data, is_centroid != 0))
    }

    /// Retrieve average spectrum calculated across a scan range.
    pub fn get_average_spectrum(&self, first_scan: i32, last_scan: i32) -> Result<RawSpectrum> {
        let mut c_data = CRawSpectrumData {
            masses: ptr::null_mut(),
            intensities: ptr::null_mut(),
            count: 0,
        };
        let mut is_centroid: c_int = 0;

        let code = unsafe {
            raw_get_average_spectrum(self.handle, first_scan, last_scan, &mut c_data, &mut is_centroid)
        };

        if code != 0 {
            return Err(RawErrorCode::from_code(code));
        }

        Ok(c_spectrum_to_raw(&mut c_data, is_centroid != 0))
    }

    /// Retrieve chromatogram trace (BasePeak, TIC, or MassRange).
    pub fn get_chromatogram(
        &self,
        trace_type: RawTraceType,
        start_scan: Option<i32>,
        end_scan: Option<i32>,
        filter: Option<&str>,
    ) -> Result<RawChromatogram> {
        let mut c_chrom = CRawChromatogramData {
            times: ptr::null_mut(),
            intensities: ptr::null_mut(),
            count: 0,
        };

        let c_filter = match filter {
            Some(f) => Some(CString::new(f).map_err(|_| RawErrorCode::NullPointer)?),
            None => None,
        };
        let filter_ptr = c_filter.as_ref().map_or(ptr::null(), |c| c.as_ptr());

        let code = unsafe {
            raw_get_chromatogram(
                self.handle,
                trace_type as c_int,
                start_scan.unwrap_or(0),
                end_scan.unwrap_or(0),
                filter_ptr,
                &mut c_chrom,
            )
        };

        if code != 0 {
            return Err(RawErrorCode::from_code(code));
        }

        let times = if c_chrom.count > 0 && !c_chrom.times.is_null() {
            unsafe { std::slice::from_raw_parts(c_chrom.times, c_chrom.count as usize).to_vec() }
        } else {
            Vec::new()
        };

        let intensities = if c_chrom.count > 0 && !c_chrom.intensities.is_null() {
            unsafe { std::slice::from_raw_parts(c_chrom.intensities, c_chrom.count as usize).to_vec() }
        } else {
            Vec::new()
        };

        unsafe {
            raw_free_chromatogram_data(&mut c_chrom);
        }

        Ok(RawChromatogram { times, intensities })
    }

    /// Retrieve Total Ion Current (TIC) chromatogram for the entire run.
    pub fn get_tic_chromatogram(&self) -> Result<RawChromatogram> {
        self.get_chromatogram(RawTraceType::Tic, None, None, None)
    }

    /// Retrieve Base Peak (BPC) chromatogram for the entire run.
    pub fn get_bpc_chromatogram(&self) -> Result<RawChromatogram> {
        self.get_chromatogram(RawTraceType::BasePeak, None, None, None)
    }
}

unsafe impl Send for RawFileSession {}
// Note: IRawDataPlus is a thread accessor tied to internal thread-local state.
// To share a single session across threads safely, wrap in Arc<Mutex<RawFileSession>>,
// or use worker-local sessions (one RawFileSession per worker thread) for maximum throughput.

impl Drop for RawFileSession {
    fn drop(&mut self) {
        if !self.handle.is_null() {
            unsafe {
                raw_close_file(self.handle);
            }
            self.handle = ptr::null_mut();
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_file_not_found() {
        let result = RawFileSession::open("nonexistent_test_file.raw");
        assert!(result.is_err());
        assert_eq!(result.unwrap_err(), RawErrorCode::FileNotFound);
    }

    #[test]
    fn test_real_raw_file() {
        let raw_path = "examples/dda_tmt_example.raw";
        if !std::path::Path::new(raw_path).exists() {
            eprintln!("Skipping test_real_raw_file: file does not exist");
            return;
        }

        let session = RawFileSession::open(raw_path).expect("Failed to open real RAW file");
        assert_eq!(session.get_first_scan().unwrap(), 1);
        assert_eq!(session.get_last_scan().unwrap(), 10447);
        assert_eq!(session.get_spectra_count().unwrap(), 10447);

        let (rt_start, rt_end) = session.get_retention_time_range().unwrap();
        assert!(rt_start >= 0.0);
        assert!(rt_end > 20.0);

        // Metadata checks
        let model = session.get_instrument_model().unwrap();
        assert_eq!(model, "Orbitrap Ascend");

        let inst_name = session.get_instrument_name().unwrap();
        assert!(!inst_name.is_empty());

        let serial = session.get_instrument_serial_number().unwrap();
        assert!(!serial.is_empty());

        let inst_count = session.get_instrument_count().unwrap();
        assert!(inst_count >= 1);

        let filter_count = session.get_filter_count().unwrap();
        assert!(filter_count >= 1);

        let res = session.get_mass_resolution().unwrap();
        assert!(res >= 0.0);

        let creation_date = session.get_creation_date().unwrap();
        assert!(!creation_date.is_empty());

        let file_name = session.get_file_name().unwrap();
        assert!(!file_name.is_empty());

        let sample_info = session.get_sample_info().unwrap();
        println!("Sample Info: {:?}", sample_info);

        // Retention time to scan number mapping
        let scan_for_rt = session.get_scan_number_from_retention_time(rt_start).unwrap();
        assert_eq!(scan_for_rt, 1);

        // Scan 1: MS1
        let stats1 = session.get_scan_stats(1).unwrap();
        assert_eq!(stats1.ms_level, 1);
        let filter1 = session.get_scan_filter(1).unwrap();
        assert!(filter1.contains("Full ms"));

        // MS1 has no precursor info -> should return Err(RawErrorCode::NoPrecursorInfo)
        let prec1 = session.get_precursor_info(1);
        assert_eq!(prec1.unwrap_err(), RawErrorCode::NoPrecursorInfo);

        // Scan 1 spectrum via get_spectrum (best)
        let spec1 = session.get_spectrum(1).unwrap();
        assert!(spec1.masses.len() > 0);
        assert_eq!(spec1.masses.len(), spec1.intensities.len());

        // Segmented scan test
        let seg1 = session.get_segmented_scan(1).unwrap();
        assert!(seg1.masses.len() > 0);

        // Centroid stream test
        let cent1 = session.get_centroid_stream(1).unwrap();
        assert!(cent1.masses.len() > 0);
        assert!(cent1.is_centroid);

        // Scan 3: MS2
        let stats3 = session.get_scan_stats(3).unwrap();
        assert_eq!(stats3.ms_level, 2);
        let p3 = session.get_precursor_info(3).unwrap();
        assert!((p3.precursor_mass - 451.8001).abs() < 0.01);
        assert!((p3.isolation_width - 0.70).abs() < 0.01);
        assert!(p3.collision_energy > 0.0, "Expected collision energy > 0, got {}", p3.collision_energy);

        // Scan MS3 check (if present)
        let mut found_ms3 = false;
        for s in 1..=100 {
            let stats = session.get_scan_stats(s).unwrap();
            if stats.ms_level == 3 {
                let p = session.get_precursor_info(s).unwrap();
                assert!(p.precursor_mass > 0.0, "MS3 scan {} precursor mass should be > 0", s);
                assert!(p.isolation_width > 0.0, "MS3 scan {} isolation width should be > 0", s);
                assert!(p.collision_energy > 0.0, "MS3 scan {} collision energy should be > 0", s);
                found_ms3 = true;
                break;
            }
        }
        assert!(found_ms3, "Expected at least one MS3 scan in first 100 scans");

        // Trailer Extra tests
        let te_count = session.get_trailer_extra_count(3).unwrap();
        assert!(te_count > 0);
        let (label0, val0) = session.get_trailer_extra_item(3, 0).unwrap();
        assert!(!label0.is_empty());
        assert!(!val0.is_empty());
        let all_te = session.get_all_trailer_extra(3).unwrap();
        assert_eq!(all_te.len() as i32, te_count);

        let master_scan_val = session.get_trailer_extra_value(3, "Master Scan").unwrap();
        assert!(!master_scan_val.is_empty());

        // Chromatogram tests (BPC & TIC)
        let bpc = session.get_bpc_chromatogram().unwrap();
        assert!(bpc.times.len() > 0);
        assert_eq!(bpc.times.len(), bpc.intensities.len());

        let tic = session.get_tic_chromatogram().unwrap();
        assert!(tic.times.len() > 0);
        assert_eq!(tic.times.len(), tic.intensities.len());

        // Average spectrum test across scans 1-5
        let avg_spec = session.get_average_spectrum(1, 5).unwrap();
        assert!(avg_spec.masses.len() > 0);
        assert_eq!(avg_spec.masses.len(), avg_spec.intensities.len());
    }

    #[test]
    fn test_parallel_processing_std_threads() {
        use std::sync::{Arc, Mutex};
        use std::thread;

        let raw_path = "examples/dda_tmt_example.raw";
        if !std::path::Path::new(raw_path).exists() {
            eprintln!("Skipping test: file does not exist");
            return;
        }

        // Pattern 1: Worker-local sessions (one handle per thread pool worker)
        // This is the optimal pattern for maximum throughput without lock contention.
        let mut handles = Vec::new();
        for chunk_idx in 0..4 {
            let path_str = raw_path.to_string();
            handles.push(thread::spawn(move || {
                let local_session = RawFileSession::open(&path_str).expect("Failed to open worker session");
                let start_scan = 1 + chunk_idx * 25;
                let end_scan = start_scan + 24;
                let mut spectra = Vec::new();
                for scan in start_scan..=end_scan {
                    let spec = local_session.get_spectrum(scan).expect("get_spectrum failed on thread-local session");
                    assert!(spec.masses.len() > 0);
                    spectra.push(spec);
                }
                spectra
            }));
        }

        let mut total_local = 0;
        for h in handles {
            let res = h.join().expect("Worker thread panicked");
            total_local += res.len();
        }
        assert_eq!(total_local, 100);

        // Pattern 2: Mutex-protected shared session
        let shared_session = Arc::new(Mutex::new(RawFileSession::open(raw_path).expect("Failed to open RAW file")));
        let mut handles2 = Vec::new();
        for chunk_idx in 0..4 {
            let session_clone = Arc::clone(&shared_session);
            handles2.push(thread::spawn(move || {
                let start_scan = 101 + chunk_idx * 25;
                let end_scan = start_scan + 24;
                let mut spectra = Vec::new();
                for scan in start_scan..=end_scan {
                    let session_guard = session_clone.lock().unwrap();
                    let spec = session_guard.get_spectrum(scan).expect("get_spectrum failed on shared session");
                    assert!(spec.masses.len() > 0);
                    spectra.push(spec);
                }
                spectra
            }));
        }

        let mut total_shared = 0;
        for h in handles2 {
            let res = h.join().expect("Worker thread panicked");
            total_shared += res.len();
        }
        assert_eq!(total_shared, 100);
    }
}
