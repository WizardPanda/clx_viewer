#ifndef CLX_THUMBNAIL_PROVIDER_H
#define CLX_THUMBNAIL_PROVIDER_H

#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <shlobj.h>
#include <shlwapi.h>
#include <thumbcache.h>

#include <string>
#include <vector>

// {6CF20D3A-B6A8-4DCB-8305-973E1B65E7B6}
DEFINE_GUID(CLSID_ClxThumbnailProvider, 0x6cf20d3a, 0xb6a8, 0x4dcb, 0x83, 0x05,
            0x97, 0x3e, 0x1b, 0x65, 0xe7, 0xb6);

// {E357FCCD-A995-4576-B01F-234630154E96} - IThumbnailProvider shellex key
DEFINE_GUID(CLSID_ThumbnailHandlerKey, 0xe357fccd, 0xa995, 0x4576, 0xb0, 0x1f,
            0x23, 0x46, 0x30, 0x15, 0x4e, 0x96);

class ClxThumbnailProvider
    : public IThumbnailProvider,
      public IInitializeWithFile,
      public IInitializeWithStream {
 public:
  ClxThumbnailProvider();
  virtual ~ClxThumbnailProvider();

  // IUnknown
  IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
  IFACEMETHODIMP_(ULONG) AddRef() override;
  IFACEMETHODIMP_(ULONG) Release() override;

  // IInitializeWithFile
  IFACEMETHODIMP Initialize(LPCWSTR pszFilePath, DWORD grfMode) override;

  // IInitializeWithStream
  IFACEMETHODIMP Initialize(IStream* pStream, DWORD grfMode) override;

  // IThumbnailProvider
  IFACEMETHODIMP GetThumbnail(UINT cx, HBITMAP* phbmp,
                              WTS_ALPHATYPE* pdwAlpha) override;

 private:
  long ref_count_;
  std::wstring path_;
  std::vector<BYTE> stream_bytes_;  // used when initialized from a stream
  bool has_stream_;

  HRESULT RenderThumbnail(UINT cx, HBITMAP* phbmp, WTS_ALPHATYPE* pdwAlpha,
                          const std::vector<BYTE>& clx_bytes);
};

class ClxThumbnailClassFactory : public IClassFactory {
 public:
  ClxThumbnailClassFactory();
  virtual ~ClxThumbnailClassFactory();

  // IUnknown
  IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
  IFACEMETHODIMP_(ULONG) AddRef() override;
  IFACEMETHODIMP_(ULONG) Release() override;

  // IClassFactory
  IFACEMETHODIMP CreateInstance(IUnknown* pUnkOuter, REFIID riid,
                                void** ppv) override;
  IFACEMETHODIMP LockServer(BOOL fLock) override;

 private:
  long ref_count_;
};

void LockModule();
void UnlockModule();

#endif  // CLX_THUMBNAIL_PROVIDER_H
