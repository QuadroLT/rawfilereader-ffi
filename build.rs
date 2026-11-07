use std::env;
use std::path::PathBuf;

fn main() {
    let manifest_dir = env::var("CARGO_MANIFEST_DIR").unwrap();
    let lib_dir = PathBuf::from(&manifest_dir).join("lib");

    // 1. Tell Cargo where to find the library (at build time)
    if lib_dir.exists() {
        println!("cargo:rustc-link-search=native={}", lib_dir.display());
    }

    let profile = env::var("PROFILE").unwrap_or_else(|_| "debug".to_string());
    let target_dir = PathBuf::from(&manifest_dir).join("target").join(&profile);
    println!("cargo:rustc-link-search=native={}", target_dir.display());

    println!("cargo:rustc-link-lib=dylib=rawfilereader");

    // 2. Instruct linker to search runtime path for the dylib/so
    let target_os = env::var("CARGO_CFG_TARGET_OS").unwrap_or_default();
    if target_os == "macos" {
        println!("cargo:rustc-link-arg=-Wl,-rpath,@loader_path");
        if lib_dir.exists() {
            println!("cargo:rustc-link-arg=-Wl,-rpath,{}", lib_dir.display());
        }
    } else if target_os == "linux" {
        println!("cargo:rustc-link-arg=-Wl,-rpath,$ORIGIN");
        if lib_dir.exists() {
            println!("cargo:rustc-link-arg=-Wl,-rpath,{}", lib_dir.display());
        }
    }

    println!("cargo:rerun-if-changed=build.rs");
    if lib_dir.exists() {
        println!("cargo:rerun-if-changed={}", lib_dir.display());
    }
}
