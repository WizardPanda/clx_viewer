#include "thumbnail_provider.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <fstream>
#include <new>

#include <gdiplus.h>
#pragma comment(lib, "gdiplus.lib")

#include "clxcpp/clx.hpp"// ---------------------------------------------------------------------------
// Auto-level constants shared with the WPF viewer. Keep in sync with
// viewer/Services/AutoLevels.cs. The fluorescence channel is shown reverted
// (western-blot style): bright signal -> black bands, dim background -> light
// gray. The gain pulls the background down from pure white so it is "clean
// but not blank".
// ---------------------------------------------------------------------------
static constexpr double kFluoPLow = 1.0;    // percentile for the noise floor
static constexpr double kFluoPHigh = 99.9;  // percentile above band peaks
static constexpr double kFluoGain = 0.92;   // top-end compression (non-blank bg)

namespace {

// Never shut GDI+ down: this DLL lives for the whole host process.
bool EnsureGdiplus() {
  static bool ok = [] {
    Gdiplus::GdiplusStartupInput input;
    ULONG_PTR token = 0;
    if (Gdiplus::GdiplusStartup(&token, &input, nullptr) == Gdiplus::Ok) {
      return true;
    }
    return false;
  }();
  return ok;
}

bool ReadFileToBytes(const std::wstring& path, std::vector<BYTE>& out) {
  std::ifstream in(path, std::ios::binary);
  if (!in) return false;
  in.seekg(0, std::ios::end);
  std::streamsize size = in.tellg();
  in.seekg(0, std::ios::beg);
  out.resize(static_cast<std::size_t>(size));
  if (size > 0) {
    in.read(reinterpret_cast<char*>(out.data()), size);
  }
  return !in.fail() || size == 0;
}

struct Histogram {
  std::vector<uint64_t> h;
  std::size_t n = 0;
};

Histogram BuildHistogram(const std::vector<uint8_t>& px, int bits) {
  Histogram out;
  std::size_t bins = bits == 8 ? 256u : 65536u;
  out.h.assign(bins, 0);
  if (bits == 16) {
    for (std::size_t i = 0; i + 1 < px.size(); i += 2) {
      ++out.h[static_cast<uint16_t>(px[i]) |
              (static_cast<uint16_t>(px[i + 1]) << 8)];
      ++out.n;
    }
  } else {
    for (uint8_t v : px) {
      ++out.h[v];
      ++out.n;
    }
  }
  return out;
}

uint64_t ValueAtRank(const Histogram& hist, std::size_t rank) {
  uint64_t cum = 0;
  for (std::size_t v = 0; v < hist.h.size(); ++v) {
    cum += hist.h[v];
    if (cum > rank) return v;
  }
  return hist.h.size() - 1;
}

// numpy.percentile(..., 'linear') on the histogram (same as clxcpp preview).
double Percentile(const Histogram& hist, double q) {
  if (hist.n == 0) return 0.0;
  double idx = static_cast<double>(hist.n - 1) * q / 100.0;
  std::size_t lo = static_cast<std::size_t>(std::floor(idx));
  std::size_t hi = static_cast<std::size_t>(std::ceil(idx));
  double frac = idx - std::floor(idx);
  double a = static_cast<double>(ValueAtRank(hist, lo));
  double b = static_cast<double>(ValueAtRank(hist, hi));
  return a + (b - a) * frac;
}

}  // namespace

// ---------------------------------------------------------------------------
// ClxThumbnailProvider
// ---------------------------------------------------------------------------

ClxThumbnailProvider::ClxThumbnailProvider()
    : ref_count_(1), has_stream_(false) {}

ClxThumbnailProvider::~ClxThumbnailProvider() = default;

IFACEMETHODIMP ClxThumbnailProvider::QueryInterface(REFIID riid, void** ppv) {
  static const QITAB qit[] = {
      QITABENT(ClxThumbnailProvider, IThumbnailProvider),
      QITABENT(ClxThumbnailProvider, IInitializeWithFile),
      QITABENT(ClxThumbnailProvider, IInitializeWithStream),
      {0},
  };
  return QISearch(this, qit, riid, ppv);
}

IFACEMETHODIMP_(ULONG) ClxThumbnailProvider::AddRef() {
  return InterlockedIncrement(&ref_count_);
}

IFACEMETHODIMP_(ULONG) ClxThumbnailProvider::Release() {
  ULONG c = InterlockedDecrement(&ref_count_);
  if (c == 0) delete this;
  return c;
}

IFACEMETHODIMP ClxThumbnailProvider::Initialize(LPCWSTR pszFilePath,
                                                DWORD /*grfMode*/) {
  if (!pszFilePath) return E_POINTER;
  path_ = pszFilePath;
  stream_bytes_.clear();
  has_stream_ = false;
  return S_OK;
}

IFACEMETHODIMP ClxThumbnailProvider::Initialize(IStream* pStream,
                                                DWORD /*grfMode*/) {
  if (!pStream) return E_POINTER;
  path_.clear();
  stream_bytes_.clear();
  has_stream_ = false;

  // Copy the whole stream into memory (clx files are a few MB at most).
  const ULONG kChunk = 64 * 1024;
  for (;;) {
    std::size_t old = stream_bytes_.size();
    stream_bytes_.resize(old + kChunk);
    ULONG read = 0;
    HRESULT hr =
        pStream->Read(stream_bytes_.data() + old, kChunk, &read);
    if (FAILED(hr)) return hr;
    stream_bytes_.resize(old + read);
    if (read < kChunk) break;
  }
  has_stream_ = true;
  return S_OK;
}

IFACEMETHODIMP ClxThumbnailProvider::GetThumbnail(UINT cx, HBITMAP* phbmp,
                                                  WTS_ALPHATYPE* pdwAlpha) {
  if (!phbmp || !pdwAlpha) return E_POINTER;
  *phbmp = nullptr;
  *pdwAlpha = WTSAT_UNKNOWN;

  std::vector<BYTE> bytes;
  if (has_stream_) {
    bytes = stream_bytes_;
  } else if (!path_.empty()) {
    if (!ReadFileToBytes(path_, bytes)) return E_FAIL;
  } else {
    return E_FAIL;
  }

  if (bytes.empty()) return E_FAIL;
  return RenderThumbnail(cx, phbmp, pdwAlpha, bytes);
}

HRESULT ClxThumbnailProvider::RenderThumbnail(
    UINT cx, HBITMAP* phbmp, WTS_ALPHATYPE* pdwAlpha,
    const std::vector<BYTE>& clx_bytes) {
  if (!EnsureGdiplus()) return E_FAIL;

  clxcpp::clx_file f;
  try {
    f = clxcpp::parse(clx_bytes);
  } catch (const std::exception&) {
    return E_FAIL;
  }

  if (f.images.empty()) return E_FAIL;

  // Prefer the fluorescence channel (reverted western-blot look), else image 0.
  int img_index = 0;
  auto labels = f.channel_labels();
  for (const auto& img : f.images) {
    auto it = labels.find(img.index);
    if (it != labels.end() && it->second == "fluorescence") {
      img_index = img.index;
      break;
    }
  }
  const clxcpp::clx_image& img = f.images[static_cast<std::size_t>(img_index)];
  if (img.bits_per_sample() != 16) return E_FAIL;
  if (img.pixel_buf.empty()) return E_FAIL;

  // Auto levels (same algorithm as the viewer).
  auto hist = BuildHistogram(img.pixel_buf, 16);
  double low = Percentile(hist, kFluoPLow);
  double high = Percentile(hist, kFluoPHigh);
  if (high <= low) high = low + 1.0;
  if (high > static_cast<double>(img.max_value())) {
    high = static_cast<double>(img.max_value());
  }
  if (high <= low) return E_FAIL;

  double scale = 255.0 / (high - low) * kFluoGain;

  // Pre-downsample so very large images don't pay full cost per pixel.
  const int64_t max_dim = std::max(img.width(), img.height());
  int stride = static_cast<int>(std::ceil(
      static_cast<double>(max_dim) / (static_cast<double>(cx) * 1.5)));
  if (stride < 1) stride = 1;

  int sw = static_cast<int>(img.width() / stride);
  int sh = static_cast<int>(img.height() / stride);
  if (sw < 1) sw = 1;
  if (sh < 1) sh = 1;

  Gdiplus::Bitmap scaled(sw, sh, PixelFormat32bppPARGB);
  Gdiplus::Rect rect(0, 0, sw, sh);
  Gdiplus::BitmapData bd;
  if (scaled.LockBits(&rect, Gdiplus::ImageLockModeWrite, PixelFormat32bppPARGB,
                      &bd) != Gdiplus::Ok) {
    return E_FAIL;
  }
  const uint8_t* px = img.pixel_buf.data();
  for (int y = 0; y < sh; ++y) {
    BYTE* row = static_cast<BYTE*>(bd.Scan0) +
                static_cast<LONG>(y) * bd.Stride;
    for (int x = 0; x < sw; ++x) {
      const uint8_t* p =
          px + (static_cast<std::size_t>(y * stride) * img.width() +
                static_cast<std::size_t>(x * stride)) *
                   2;
      uint16_t v = static_cast<uint16_t>(p[0]) |
                   (static_cast<uint16_t>(p[1]) << 8);
      double m = (high - static_cast<double>(v)) * scale;
      m = std::max(0.0, std::min(255.0, m));
      BYTE g = static_cast<BYTE>(std::lround(m));
      row[x * 4 + 0] = g;      // B
      row[x * 4 + 1] = g;      // G
      row[x * 4 + 2] = g;      // R
      row[x * 4 + 3] = 255;    // A
    }
  }
  scaled.UnlockBits(&bd);

  // Compose the final square canvas with a transparent background.
  Gdiplus::Bitmap canvas(static_cast<INT>(cx), static_cast<INT>(cx),
                         PixelFormat32bppPARGB);
  Gdiplus::Graphics g(&canvas);
  g.SetInterpolationMode(Gdiplus::InterpolationModeHighQualityBicubic);
  g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
  g.Clear(Gdiplus::Color(0, 0, 0, 0));

  // Fit image into the canvas preserving aspect ratio.
  double aspect = static_cast<double>(img.width()) / img.height();
  double dw, dh;
  if (aspect >= 1.0) {
    dw = static_cast<double>(cx) * 0.92;
    dh = dw / aspect;
  } else {
    dh = static_cast<double>(cx) * 0.92;
    dw = dh * aspect;
  }
  Gdiplus::REAL ix = static_cast<Gdiplus::REAL>((static_cast<double>(cx) - dw) / 2.0);
  Gdiplus::REAL iy = static_cast<Gdiplus::REAL>((static_cast<double>(cx) - dh) / 2.0);
  g.DrawImage(&scaled, Gdiplus::RectF(ix, iy, static_cast<Gdiplus::REAL>(dw),
                                      static_cast<Gdiplus::REAL>(dh)));

  // Hand the HBITMAP to the shell (32bpp premultiplied alpha).
  Gdiplus::Color background(0, 0, 0, 0);
  Gdiplus::Status st = canvas.GetHBITMAP(background, phbmp);
  if (st != Gdiplus::Ok) return E_FAIL;
  *pdwAlpha = WTSAT_ARGB;
  return S_OK;
}

// ---------------------------------------------------------------------------
// ClxThumbnailClassFactory
// ---------------------------------------------------------------------------

ClxThumbnailClassFactory::ClxThumbnailClassFactory() : ref_count_(1) {}
ClxThumbnailClassFactory::~ClxThumbnailClassFactory() = default;

IFACEMETHODIMP ClxThumbnailClassFactory::QueryInterface(REFIID riid,
                                                        void** ppv) {
  if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IClassFactory)) {
    *ppv = static_cast<IClassFactory*>(this);
    AddRef();
    return S_OK;
  }
  *ppv = nullptr;
  return E_NOINTERFACE;
}

IFACEMETHODIMP_(ULONG) ClxThumbnailClassFactory::AddRef() {
  return InterlockedIncrement(&ref_count_);
}

IFACEMETHODIMP_(ULONG) ClxThumbnailClassFactory::Release() {
  ULONG c = InterlockedDecrement(&ref_count_);
  if (c == 0) delete this;
  return c;
}

IFACEMETHODIMP ClxThumbnailClassFactory::CreateInstance(IUnknown* pUnkOuter,
                                                        REFIID riid,
                                                        void** ppv) {
  if (pUnkOuter) return CLASS_E_NOAGGREGATION;
  ClxThumbnailProvider* p = new (std::nothrow) ClxThumbnailProvider();
  if (!p) return E_OUTOFMEMORY;
  HRESULT hr = p->QueryInterface(riid, ppv);
  p->Release();
  return hr;
}

IFACEMETHODIMP ClxThumbnailClassFactory::LockServer(BOOL fLock) {
  if (fLock) {
    LockModule();
  } else {
    UnlockModule();
  }
  return S_OK;
}
