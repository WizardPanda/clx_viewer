add_library(clxreader SHARED clxreader.cpp clxreader.h)
target_link_libraries(clxreader PRIVATE clxcpp)
target_include_directories(clxreader PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

if(MSVC)
  target_compile_definitions(clxreader PRIVATE CLXREADER_BUILD)
  # Export a flat C ABI; keep CRT static to avoid runtime DLL dependency creep.
  set_target_properties(clxreader PROPERTIES
    PREFIX ""
    OUTPUT_NAME "clxreader"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
  )
endif()
