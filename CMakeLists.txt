cmake_minimum_required(VERSION 3.20)

project(clx_viewer VERSION 0.1.0 LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
  set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type" FORCE)
endif()

if(MSVC)
  add_compile_options(/W4 /permissive-)
else()
  add_compile_options(-Wall -Wextra)
endif()

# Reference the clxcpp library (C++ port of the Clinx .clx parser).
# Kept as the source of truth so parsing stays pixel-identical to it.
set(CLXCPP_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../clinx_format_cpp")
if(NOT EXISTS "${CLXCPP_DIR}/CMakeLists.txt")
  message(FATAL_ERROR "clxcpp not found at ${CLXCPP_DIR}. Set CLXCPP_DIR.")
endif()
add_subdirectory("${CLXCPP_DIR}" clxcpp)

add_subdirectory(native/clxreader)
add_subdirectory(native/shellext)
