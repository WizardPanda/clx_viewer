# Windows shell thumbnail provider for .clx files (IThumbnailProvider).
# Renders the fluorescence channel (reverted, auto-scaled) with a rounded
# "#012c5d" "Clx" badge and registers under HKCU via regsvr32.

add_library(shellext SHARED
  dllmain.cpp
  guids.cpp
  thumbnail_provider.cpp
  thumbnail_provider.h
)

target_include_directories(shellext PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_link_libraries(shellext PRIVATE clxcpp shlwapi gdiplus)

if(MSVC)
  set_target_properties(shellext PROPERTIES
    PREFIX ""
    OUTPUT_NAME "ClinxThumbnailProvider"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
  )
  target_compile_definitions(shellext PRIVATE _UNICODE UNICODE)
  target_sources(shellext PRIVATE shellext.def)
endif()
