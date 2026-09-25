cmake_minimum_required(VERSION 3.10)
project(UrpOrbNative LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(OpenCV REQUIRED)

add_library(UrpOrbNative SHARED
    src/urp_orb_native.cpp
)

target_include_directories(UrpOrbNative PRIVATE
    ${OpenCV_INCLUDE_DIRS}
)

target_link_libraries(UrpOrbNative
    ${OpenCV_LIBS}
    log
)

if(ANDROID AND CMAKE_ANDROID_ARCH_ABI STREQUAL "arm64-v8a")
    target_link_options(UrpOrbNative PRIVATE
        "-Wl,-z,max-page-size=16384"
        "-Wl,-z,common-page-size=16384"
    )
endif()
