cmake_minimum_required(VERSION 3.20)
project(playhouse_echo_client LANGUAGES CXX)

# C++ standard
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Build type default to Release
if(NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release)
endif()

# Compiler flags
set(CMAKE_CXX_FLAGS_RELEASE "-O3 -march=native -DNDEBUG")
set(CMAKE_CXX_FLAGS_DEBUG "-g -O0 -Wall -Wextra")

# Find required packages
# Boost ASIO is header-only, we just need the headers
find_package(Boost)
if(NOT Boost_FOUND)
    message(STATUS "Boost not found via CMake, using bundled Boost")
    set(Boost_INCLUDE_DIRS "${CMAKE_CURRENT_SOURCE_DIR}/third_party/boost_1_84_0")
endif()

message(STATUS "Boost include dirs: ${Boost_INCLUDE_DIRS}")

find_package(Threads REQUIRED)

# Add proto subdirectory
add_subdirectory(proto)

# ASIO layer source files
set(ASIO_SOURCES
    asio/asio.system.cpp
    asio/asio.Nstatistics.cpp
    asio/asio.Isocket_tcp.cpp
    asio/asio.Nsocket_tcp.cpp
    asio/asio.Nsocket_tcp_client.cpp
    asio/asio.Nconnective.cpp
    asio/asio.Nconnector.cpp
)

# Create ASIO library
add_library(asio_layer STATIC ${ASIO_SOURCES})

target_include_directories(asio_layer PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${Boost_INCLUDE_DIRS}
)

target_link_libraries(asio_layer PUBLIC
    Threads::Threads
)

# Link Boost if found
if(Boost_FOUND)
    target_link_libraries(asio_layer PUBLIC Boost::system)
endif()

target_compile_features(asio_layer PUBLIC cxx_std_20)

# Protocol layer source files
set(PROTOCOL_SOURCES
    protocol/playhouse_codec.cpp
)

# Create protocol library
add_library(protocol STATIC ${PROTOCOL_SOURCES})

target_include_directories(protocol PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}
)

target_compile_features(protocol PUBLIC cxx_std_20)

# Util source files
set(UTIL_SOURCES
    util/util.linux.cpp
)

# Create util library
add_library(util STATIC ${UTIL_SOURCES})
target_compile_features(util PUBLIC cxx_std_20)

# Common client source files
set(CLIENT_COMMON_SOURCES
    client/playhouse_socket.cpp
    client/echo_client.cpp
)

# Interactive client source files
set(CLIENT_INTERACTIVE_SOURCES
    ${CLIENT_COMMON_SOURCES}
    client/main.cpp
)

# Benchmark client source files
set(CLIENT_BENCHMARK_SOURCES
    ${CLIENT_COMMON_SOURCES}
    client/benchmark_main.cpp
)

# Interactive executable
add_executable(echo_client ${CLIENT_INTERACTIVE_SOURCES})

target_include_directories(echo_client PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${Boost_INCLUDE_DIRS}
)

target_link_libraries(echo_client PRIVATE
    asio_layer
    protocol
    echo_proto
    util
    Threads::Threads
)

# Link Boost if found
if(Boost_FOUND)
    target_link_libraries(echo_client PRIVATE Boost::system)
endif()

target_compile_features(echo_client PUBLIC cxx_std_20)

# Benchmark executable
add_executable(echo_benchmark ${CLIENT_BENCHMARK_SOURCES})

target_include_directories(echo_benchmark PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${Boost_INCLUDE_DIRS}
)

target_link_libraries(echo_benchmark PRIVATE
    asio_layer
    protocol
    echo_proto
    util
    Threads::Threads
)

# Link Boost if found
if(Boost_FOUND)
    target_link_libraries(echo_benchmark PRIVATE Boost::system)
endif()

target_compile_features(echo_benchmark PUBLIC cxx_std_20)

# Print configuration info
message(STATUS "Build type: ${CMAKE_BUILD_TYPE}")
message(STATUS "C++ standard: ${CMAKE_CXX_STANDARD}")
message(STATUS "Boost version: ${Boost_VERSION}")
message(STATUS "Compiler: ${CMAKE_CXX_COMPILER_ID} ${CMAKE_CXX_COMPILER_VERSION}")
