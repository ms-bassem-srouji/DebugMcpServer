cmake_minimum_required(VERSION 3.10)
project(NativeCrashTarget LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)

add_executable(NativeCrashTarget main.cpp)

# Link dbghelp for MiniDumpWriteDump on Windows
if(WIN32)
    target_link_libraries(NativeCrashTarget dbghelp)
endif()

# Debug symbols for meaningful stack traces
if(MSVC)
    target_compile_options(NativeCrashTarget PRIVATE /Zi /Od)
    target_link_options(NativeCrashTarget PRIVATE /DEBUG)
elseif(CMAKE_CXX_COMPILER_ID MATCHES "GNU|Clang")
    target_compile_options(NativeCrashTarget PRIVATE -g -O0)
endif()
