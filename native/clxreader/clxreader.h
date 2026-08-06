#ifndef CLXREADER_H
#define CLXREADER_H

// C ABI over clxcpp, consumed by the .NET WPF viewer via P/Invoke.
// All functions are __cdecl. Strings are UTF-8 and written into caller
// buffers; there are no allocated-out params to avoid managed/native
// ownership issues.

#ifdef __cplusplus
extern "C" {
#endif

#ifdef CLXREADER_BUILD
#define CLXREADER_API __declspec(dllexport)
#else
#define CLXREADER_API __declspec(dllimport)
#endif

// ---- opaque file handle -------------------------------------------------

typedef void* clxr_file;

// Parse a .clx file. Returns NULL on failure (call clxr_last_error for text).
CLXREADER_API clxr_file clxr_open(const char* path);
CLXREADER_API void clxr_close(clxr_file f);

// Copy the last error message (UTF-8) into buf (nul-terminated, truncated).
CLXREADER_API void clxr_last_error(char* buf, int buf_len);

// ---- per-file info ------------------------------------------------------

CLXREADER_API int clxr_image_count(clxr_file f);

// File stem (path without extension), used for default export names.
// Returns the number of chars needed (excluding NUL) if buf is too small.
CLXREADER_API int clxr_stem(clxr_file f, char* buf, int buf_len);

// ---- per-image info -----------------------------------------------------

// Struct matching clxcpp image_descriptor + index (all 64-bit, no padding).
typedef struct clxr_image_info {
  long long index;
  long long offset;
  long long type;
  long long width;
  long long height;
  long long bits_per_sample;
  long long min_value;
  long long max_value;
  long long byte_count;
  int channel;          // 0 = brightfield, 1 = fluorescence, -1 = unknown
} clxr_image_info;

// Fill info with data for image i. Returns 1 on success, 0 if out of range.
CLXREADER_API int clxr_image_info_get(clxr_file f, int i, clxr_image_info* info);

// Copy raw little-endian pixels (width*height*2 bytes) into dst (>= byte_count).
// Returns 1 on success, 0 if index invalid or buffer too small (pass 0 to query).
CLXREADER_API int clxr_copy_image_pixels(clxr_file f, int i,
                                         unsigned char* dst, long long dst_cap);

// ---- metadata -----------------------------------------------------------

// Full clxcpp to_json() output (curated by the viewer). Returns needed length.
CLXREADER_API int clxr_metadata_json(clxr_file f, char* buf, int buf_len);

// ---- export (pixel-identical TIFF via clxcpp) ---------------------------

// Save raw 16-bit image i as a TIFF next to the source (or to path).
// Returns 1 on success.
CLXREADER_API int clxr_save_tiff(clxr_file f, int i, const char* path);

#ifdef __cplusplus
}
#endif

#endif  // CLXREADER_H
