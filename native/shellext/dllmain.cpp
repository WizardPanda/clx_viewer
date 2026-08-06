#include "thumbnail_provider.h"

#include <new>

// Module lifetime: counts outstanding class factories so the host process can
// unload us when idle. No need to track provider instances themselves.
static volatile LONG g_module_lock_count = 0;
static HMODULE g_hinst = nullptr;

void LockModule() { InterlockedIncrement(&g_module_lock_count); }
void UnlockModule() { InterlockedDecrement(&g_module_lock_count); }

BOOL WINAPI DllMain(HINSTANCE hinst, DWORD reason, LPVOID /*reserved*/) {
  switch (reason) {
    case DLL_PROCESS_ATTACH:
      g_hinst = hinst;
      DisableThreadLibraryCalls(hinst);
      break;
    default:
      break;
  }
  return TRUE;
}

STDAPI DllCanUnloadNow() {
  return g_module_lock_count == 0 ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, LPVOID* ppv) {
  if (ppv == nullptr) return E_POINTER;
  *ppv = nullptr;

  HRESULT hr = CLASS_E_CLASSNOTAVAILABLE;
  if (IsEqualCLSID(rclsid, CLSID_ClxThumbnailProvider)) {
    ClxThumbnailClassFactory* factory =
        new (std::nothrow) ClxThumbnailClassFactory();
    if (factory == nullptr) return E_OUTOFMEMORY;
    hr = factory->QueryInterface(riid, ppv);
    factory->Release();
  }
  return hr;
}

// Registers the thumbnail handler for .clx under the current user (HKCU) so
// no admin rights are needed. The .clx file-type association itself is set up
// by register.ps1 (it needs to know the viewer install path).
STDAPI DllRegisterServer() {
  wchar_t dll_path[MAX_PATH] = {0};
  if (!GetModuleFileNameW(g_hinst, dll_path, MAX_PATH)) return E_FAIL;

  wchar_t clsid[MAX_PATH] = {0};
  StringFromGUID2(CLSID_ClxThumbnailProvider, clsid, ARRAYSIZE(clsid));
  wchar_t thumb_key[MAX_PATH] = {0};
  StringFromGUID2(CLSID_ThumbnailHandlerKey, thumb_key, ARRAYSIZE(thumb_key));

  HKEY root = nullptr;
  LONG res = RegCreateKeyExW(HKEY_CURRENT_USER,
                             L"Software\\Classes\\CLSID", 0, nullptr, 0,
                             KEY_WRITE, nullptr, &root, nullptr);
  if (res != ERROR_SUCCESS) return HRESULT_FROM_WIN32(res);

  std::wstring clsid_path = L"Software\\Classes\\CLSID\\";
  clsid_path += clsid;
  HKEY clsid_key = nullptr;
  res = RegCreateKeyExW(HKEY_CURRENT_USER, clsid_path.c_str(), 0, nullptr, 0,
                        KEY_WRITE, nullptr, &clsid_key, nullptr);
  if (res != ERROR_SUCCESS) {
    RegCloseKey(root);
    return HRESULT_FROM_WIN32(res);
  }

  HKEY inproc = nullptr;
  res = RegCreateKeyExW(clsid_key, L"InprocServer32", 0, nullptr, 0, KEY_WRITE,
                        nullptr, &inproc, nullptr);
  if (res == ERROR_SUCCESS) {
    RegSetValueExW(inproc, nullptr, 0, REG_SZ,
                   reinterpret_cast<const BYTE*>(dll_path),
                   static_cast<DWORD>((wcslen(dll_path) + 1) * sizeof(wchar_t)));
    RegSetValueExW(inproc, L"ThreadingModel", 0, REG_SZ,
                   reinterpret_cast<const BYTE*>(L"Apartment"),
                   static_cast<DWORD>(20));
    RegCloseKey(inproc);
  }

  // .clx -> shellex\{E357FCCD-...} -> our CLSID (IThumbnailProvider).
  std::wstring ext_shellex =
      L"Software\\Classes\\.clx\\shellex\\" + std::wstring(thumb_key);
  HKEY ext_key = nullptr;
  res = RegCreateKeyExW(HKEY_CURRENT_USER, ext_shellex.c_str(), 0, nullptr, 0,
                        KEY_WRITE, nullptr, &ext_key, nullptr);
  if (res == ERROR_SUCCESS) {
    RegSetValueExW(ext_key, nullptr, 0, REG_SZ,
                   reinterpret_cast<const BYTE*>(clsid),
                   static_cast<DWORD>((wcslen(clsid) + 1) * sizeof(wchar_t)));
    RegCloseKey(ext_key);
  }

  // Alias key for shells that look up "ThumbnailHandler" by name.
  std::wstring alias_path = L"Software\\Classes\\.clx\\shellex\\ThumbnailHandler";
  HKEY alias_key = nullptr;
  res = RegCreateKeyExW(HKEY_CURRENT_USER, alias_path.c_str(), 0, nullptr, 0,
                        KEY_WRITE, nullptr, &alias_key, nullptr);
  if (res == ERROR_SUCCESS) {
    RegSetValueExW(alias_key, nullptr, 0, REG_SZ,
                   reinterpret_cast<const BYTE*>(clsid),
                   static_cast<DWORD>((wcslen(clsid) + 1) * sizeof(wchar_t)));
    RegCloseKey(alias_key);
  }

  RegCloseKey(clsid_key);
  RegCloseKey(root);

  SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);
  return S_OK;
}

STDAPI DllUnregisterServer() {
  wchar_t clsid[MAX_PATH] = {0};
  StringFromGUID2(CLSID_ClxThumbnailProvider, clsid, ARRAYSIZE(clsid));
  wchar_t thumb_key[MAX_PATH] = {0};
  StringFromGUID2(CLSID_ThumbnailHandlerKey, thumb_key, ARRAYSIZE(thumb_key));

  std::wstring clsid_path = L"Software\\Classes\\CLSID\\";
  clsid_path += clsid;
  RegDeleteTreeW(HKEY_CURRENT_USER, clsid_path.c_str());

  std::wstring ext_shellex =
      L"Software\\Classes\\.clx\\shellex\\" + std::wstring(thumb_key);
  RegDeleteTreeW(HKEY_CURRENT_USER, ext_shellex.c_str());
  RegDeleteTreeW(HKEY_CURRENT_USER,
                 L"Software\\Classes\\.clx\\shellex\\ThumbnailHandler");

  SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);
  return S_OK;
}



