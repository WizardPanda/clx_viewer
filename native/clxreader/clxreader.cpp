#include "clxreader.h"

#include "clxcpp/clx.hpp"

#include <cstring>
#include <string>

namespace {

// Thread-local last-error so concurrent loads (shell/Explorer, viewer) are safe.
thread_local std::string g_error;

}  // namespace

extern "C" {

clxr_file clxr_open(const char* path) {
  g_error.clear();
  try {
    auto* f = new clxcpp::clx_file(clxcpp::load(path ? path : ""));
    return reinterpret_cast<clxr_file>(f);
  } catch (const std::exception& e) {
    g_error = e.what();
    return nullptr;
  }
}

void clxr_close(clxr_file f) {
  delete reinterpret_cast<clxcpp::clx_file*>(f);
}

void clxr_last_error(char* buf, int buf_len) {
  if (!buf || buf_len <= 0) return;
  std::size_t n = g_error.copy(buf, static_cast<std::size_t>(buf_len - 1));
  buf[n] = 0;
}

int clxr_image_count(clxr_file f) {
  auto* file = reinterpret_cast<clxcpp::clx_file*>(f);
  if (!file) return 0;
  return static_cast<int>(file->image_count());
}

int clxr_stem(clxr_file f, char* buf, int buf_len) {
  auto* file = reinterpret_cast<clxcpp::clx_file*>(f);
  if (!file) return 0;
  std::string path = file->path;
  std::size_t slash = path.find_last_of("/\\");
  if (slash != std::string::npos) path = path.substr(slash + 1);
  std::size_t dot = path.find_last_of('.');
  if (dot != std::string::npos) path = path.substr(0, dot);
  int needed = static_cast<int>(path.size());
  if (buf && buf_len > 0) {
    std::size_t n = path.copy(buf, static_cast<std::size_t>(buf_len - 1));
    buf[n] = 0;
  }
  return needed;
}

int clxr_image_info_get(clxr_file f, int i, clxr_image_info* info) {
  auto* file = reinterpret_cast<clxcpp::clx_file*>(f);
  if (!file || !info) return 0;
  if (i < 0 || static_cast<std::size_t>(i) >= file->images.size()) return 0;
  const auto& img = file->images[static_cast<std::size_t>(i)];
  info->index = img.index;
  info->offset = img.descriptor.offset;
  info->type = img.type();
  info->width = img.width();
  info->height = img.height();
  info->bits_per_sample = img.bits_per_sample();
  info->min_value = img.min_value();
  info->max_value = img.max_value();
  info->byte_count = img.byte_count();
  info->channel = -1;
  auto labels = file->channel_labels();
  auto it = labels.find(img.index);
  if (it != labels.end()) {
    info->channel = it->second == "brightfield" ? 0 : 1;
  }
  return 1;
}

int clxr_copy_image_pixels(clxr_file f, int i, unsigned char* dst,
                           long long dst_cap) {
  auto* file = reinterpret_cast<clxcpp::clx_file*>(f);
  if (!file) return 0;
  if (i < 0 || static_cast<std::size_t>(i) >= file->images.size()) return 0;
  const auto& img = file->images[static_cast<std::size_t>(i)];
  if (static_cast<long long>(img.pixel_buf.size()) > dst_cap) return 0;
  if (!dst) return 0;
  std::memcpy(dst, img.pixel_buf.data(), img.pixel_buf.size());
  return 1;
}

int clxr_metadata_json(clxr_file f, char* buf, int buf_len) {
  auto* file = reinterpret_cast<clxcpp::clx_file*>(f);
  if (!file) return 0;
  std::string json = file->to_json();
  int needed = static_cast<int>(json.size());
  if (buf && buf_len > 0) {
    std::size_t n = json.copy(buf, static_cast<std::size_t>(buf_len - 1));
    buf[n] = 0;
  }
  return needed;
}

int clxr_save_tiff(clxr_file f, int i, const char* path) {
  auto* file = reinterpret_cast<clxcpp::clx_file*>(f);
  if (!file || !path) return 0;
  if (i < 0 || static_cast<std::size_t>(i) >= file->images.size()) return 0;
  g_error.clear();
  try {
    file->images[static_cast<std::size_t>(i)].save_tiff(path);
    return 1;
  } catch (const std::exception& e) {
    g_error = e.what();
    return 0;
  }
}

}  // extern "C"
