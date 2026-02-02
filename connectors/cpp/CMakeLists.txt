cmake_minimum_required(VERSION 3.20)
project(playhouse-connector VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Compiler options
if(MSVC)
    add_compile_options(/W4 /WX)
else()
    add_compile_options(-Wall -Wextra -Wpedantic)
endif()

# Find dependencies
find_package(asio CONFIG QUIET)
if(NOT asio_FOUND)
    message(STATUS "asio not found via CMake, will use standalone asio")
    add_compile_definitions(ASIO_STANDALONE)
endif()

# Source files
set(PLAYHOUSE_SOURCES
    src/connector.cpp
    src/client_network.cpp
    src/tcp_connection.cpp
    src/packet.cpp
    src/packet_codec.cpp
    src/ring_buffer.cpp
)

# Library target
add_library(playhouse-connector ${PLAYHOUSE_SOURCES})

# Include directories
target_include_directories(playhouse-connector PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

# Link dependencies
if(asio_FOUND)
    target_link_libraries(playhouse-connector PUBLIC asio::asio)
else()
    target_compile_definitions(playhouse-connector PUBLIC ASIO_STANDALONE)
endif()

# Platform-specific libraries
if(WIN32)
    target_link_libraries(playhouse-connector PRIVATE ws2_32 wsock32)
else()
    target_link_libraries(playhouse-connector PRIVATE pthread)
endif()

# Tests
if(BUILD_TESTING)
    enable_testing()
    find_package(GTest CONFIG QUIET)
    if(GTest_FOUND)
        add_executable(connector_test tests/connector_test.cpp)
        target_link_libraries(connector_test PRIVATE playhouse-connector GTest::gtest_main)
        add_test(NAME connector_test COMMAND connector_test)
    else()
        message(STATUS "GTest not found, skipping tests")
    endif()
endif()

# Installation
install(TARGETS playhouse-connector
    EXPORT playhouse-connector-targets
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib
    RUNTIME DESTINATION bin
)

install(DIRECTORY include/ DESTINATION include)

install(EXPORT playhouse-connector-targets
    FILE playhouse-connector-targets.cmake
    NAMESPACE playhouse::
    DESTINATION lib/cmake/playhouse-connector
)

# Package config
include(CMakePackageConfigHelpers)
write_basic_package_version_file(
    "${CMAKE_CURRENT_BINARY_DIR}/playhouse-connector-config-version.cmake"
    VERSION ${PROJECT_VERSION}
    COMPATIBILITY AnyNewerVersion
)

configure_package_config_file(
    "${CMAKE_CURRENT_SOURCE_DIR}/cmake/playhouse-connector-config.cmake.in"
    "${CMAKE_CURRENT_BINARY_DIR}/playhouse-connector-config.cmake"
    INSTALL_DESTINATION lib/cmake/playhouse-connector
)

install(FILES
    "${CMAKE_CURRENT_BINARY_DIR}/playhouse-connector-config.cmake"
    "${CMAKE_CURRENT_BINARY_DIR}/playhouse-connector-config-version.cmake"
    DESTINATION lib/cmake/playhouse-connector
)
