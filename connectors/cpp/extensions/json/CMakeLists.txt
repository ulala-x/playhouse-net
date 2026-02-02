# PlayHouse JSON Extension
cmake_minimum_required(VERSION 3.20)

# Find nlohmann/json
find_package(nlohmann_json 3.11 QUIET)
if(NOT nlohmann_json_FOUND)
    message(STATUS "nlohmann/json not found, JSON extension will be skipped")
    return()
endif()

# JSON Extension library (header-only)
add_library(playhouse-json-extension INTERFACE)

target_include_directories(playhouse-json-extension INTERFACE
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

target_link_libraries(playhouse-json-extension INTERFACE
    playhouse-connector
    nlohmann_json::nlohmann_json
)

# Tests
if(BUILD_TESTING)
    find_package(GTest CONFIG QUIET)
    if(GTest_FOUND)
        add_executable(json_extensions_test tests/json_extensions_test.cpp)
        target_link_libraries(json_extensions_test PRIVATE
            playhouse-json-extension
            GTest::gtest_main
        )
        add_test(NAME json_extensions_test COMMAND json_extensions_test)
    else()
        message(STATUS "GTest not found, skipping JSON extension tests")
    endif()
endif()

# Installation
install(DIRECTORY include/ DESTINATION include)
