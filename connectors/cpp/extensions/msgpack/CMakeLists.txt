# PlayHouse MessagePack Extension
cmake_minimum_required(VERSION 3.20)

# Find msgpack-c
find_package(msgpack-cxx 6.1 QUIET)
if(NOT msgpack-cxx_FOUND)
    # Try alternative package name
    find_package(msgpack 6.1 QUIET)
    if(NOT msgpack_FOUND)
        message(STATUS "msgpack-c not found, MessagePack extension will be skipped")
        return()
    endif()
endif()

# MessagePack Extension library (header-only)
add_library(playhouse-msgpack-extension INTERFACE)

target_include_directories(playhouse-msgpack-extension INTERFACE
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

if(msgpack-cxx_FOUND)
    target_link_libraries(playhouse-msgpack-extension INTERFACE
        playhouse-connector
        msgpack-cxx
    )
else()
    target_link_libraries(playhouse-msgpack-extension INTERFACE
        playhouse-connector
        msgpack
    )
endif()

# Tests
if(BUILD_TESTING)
    find_package(GTest CONFIG QUIET)
    if(GTest_FOUND)
        add_executable(msgpack_extensions_test tests/msgpack_extensions_test.cpp)
        target_link_libraries(msgpack_extensions_test PRIVATE
            playhouse-msgpack-extension
            GTest::gtest_main
        )
        add_test(NAME msgpack_extensions_test COMMAND msgpack_extensions_test)
    else()
        message(STATUS "GTest not found, skipping MessagePack extension tests")
    endif()
endif()

# Installation
install(DIRECTORY include/ DESTINATION include)
