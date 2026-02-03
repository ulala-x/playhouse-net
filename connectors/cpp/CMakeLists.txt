cmake_minimum_required(VERSION 3.20)
project(playhouse-connector VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
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
    # Check for bundled third_party asio
    if(EXISTS ${CMAKE_CURRENT_SOURCE_DIR}/third_party/asio/asio/include)
        set(ASIO_INCLUDE_DIR ${CMAKE_CURRENT_SOURCE_DIR}/third_party/asio/asio/include)
        message(STATUS "Using bundled asio from: ${ASIO_INCLUDE_DIR}")
    endif()
endif()

# Boost (Beast) for WebSocket transport
set(BOOST_ROOT_DIR ${CMAKE_CURRENT_SOURCE_DIR}/third_party/boost)
if(EXISTS ${BOOST_ROOT_DIR})
    file(GLOB BOOST_LIB_INCLUDE_DIRS LIST_DIRECTORIES true
        ${BOOST_ROOT_DIR}/libs/*/include
        ${BOOST_ROOT_DIR}/libs/*/*/include
    )
    list(FILTER BOOST_LIB_INCLUDE_DIRS INCLUDE REGEX ".*/libs/.+/include$")
    set(BOOST_INCLUDE_DIRS ${BOOST_LIB_INCLUDE_DIRS})
    message(STATUS "Using vendored Boost headers from: ${BOOST_ROOT_DIR}")
else()
    find_package(Boost QUIET)
    if(Boost_FOUND)
        set(BOOST_INCLUDE_DIRS ${Boost_INCLUDE_DIRS})
        message(STATUS "Using system Boost headers")
    else()
        include(FetchContent)
        message(STATUS "Boost not found, fetching headers for WebSocket support")
        FetchContent_Declare(
            boost_headers
            URL https://archives.boost.io/release/1.84.0/source/boost_1_84_0.tar.gz
            DOWNLOAD_EXTRACT_TIMESTAMP true
        )
        FetchContent_GetProperties(boost_headers)
        if(NOT boost_headers_POPULATED)
            FetchContent_Populate(boost_headers)
        endif()
        file(GLOB BOOST_LIB_INCLUDE_DIRS LIST_DIRECTORIES true ${boost_headers_SOURCE_DIR}/libs/*/include)
        list(FILTER BOOST_LIB_INCLUDE_DIRS INCLUDE REGEX ".*/libs/.+/include$")
        set(BOOST_INCLUDE_DIRS ${BOOST_LIB_INCLUDE_DIRS})
    endif()
endif()

# Source files
set(PLAYHOUSE_SOURCES
    src/connector.cpp
    src/client_network.cpp
    src/tcp_connection.cpp
    src/ws_connection.cpp
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

if(BOOST_INCLUDE_DIRS)
    target_include_directories(playhouse-connector SYSTEM PRIVATE ${BOOST_INCLUDE_DIRS})
endif()

# Add asio include directory if using bundled version
if(ASIO_INCLUDE_DIR)
    target_include_directories(playhouse-connector SYSTEM PUBLIC
        $<BUILD_INTERFACE:${ASIO_INCLUDE_DIR}>
    )
endif()

# Link dependencies
if(asio_FOUND)
    target_link_libraries(playhouse-connector PUBLIC asio::asio)
else()
    target_compile_definitions(playhouse-connector PUBLIC ASIO_STANDALONE)
endif()
target_compile_definitions(playhouse-connector PRIVATE BOOST_ERROR_CODE_HEADER_ONLY)

# Platform-specific libraries
if(WIN32)
    target_link_libraries(playhouse-connector PRIVATE ws2_32 wsock32)
else()
    target_link_libraries(playhouse-connector PRIVATE pthread)
endif()

# Extensions (optional serialization support)
if(EXISTS ${CMAKE_CURRENT_SOURCE_DIR}/extensions)
    add_subdirectory(extensions)
endif()

# Tests
if(BUILD_TESTING)
    enable_testing()
    find_package(GTest CONFIG QUIET)
    if(NOT GTest_FOUND)
        # Try bundled googletest
        if(EXISTS ${CMAKE_CURRENT_SOURCE_DIR}/third_party/googletest)
            message(STATUS "Using bundled googletest")
            add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/third_party/googletest third_party/googletest EXCLUDE_FROM_ALL)
            set(GTest_FOUND TRUE)
        endif()
    endif()
    if(GTest_FOUND)
        # Basic unit tests
        add_executable(connector_test tests/connector_test.cpp)
        target_link_libraries(connector_test PRIVATE playhouse-connector GTest::gtest_main)
        add_test(NAME connector_test COMMAND connector_test)

        # Integration tests (if directory exists)
        if(EXISTS ${CMAKE_CURRENT_SOURCE_DIR}/tests/integration)
            add_subdirectory(tests/integration)
        endif()
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
