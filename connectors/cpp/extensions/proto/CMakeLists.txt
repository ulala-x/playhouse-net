# PlayHouse Protobuf Extension
cmake_minimum_required(VERSION 3.20)

# Find Protobuf
find_package(Protobuf 3.25 QUIET)
if(NOT Protobuf_FOUND)
    message(STATUS "Protobuf not found, Proto extension will be skipped")
    return()
endif()

# Protobuf Extension library (header-only)
add_library(playhouse-proto-extension INTERFACE)

target_include_directories(playhouse-proto-extension INTERFACE
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

target_link_libraries(playhouse-proto-extension INTERFACE
    playhouse-connector
    protobuf::libprotobuf
)

# Tests
if(BUILD_TESTING)
    find_package(GTest CONFIG QUIET)
    if(GTest_FOUND)
        add_executable(proto_extensions_test tests/proto_extensions_test.cpp)
        target_link_libraries(proto_extensions_test PRIVATE
            playhouse-proto-extension
            GTest::gtest_main
        )
        add_test(NAME proto_extensions_test COMMAND proto_extensions_test)
    else()
        message(STATUS "GTest not found, skipping Proto extension tests")
    endif()
endif()

# Installation
install(DIRECTORY include/ DESTINATION include)
