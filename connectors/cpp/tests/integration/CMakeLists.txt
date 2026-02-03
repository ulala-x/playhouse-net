# PlayHouse C++ Connector Integration Tests
cmake_minimum_required(VERSION 3.20)

# Check if we have GTest (bundled or package)
set(_gtest_lib "")
set(_gtest_main_lib "")
if(TARGET GTest::gtest AND TARGET GTest::gtest_main)
    set(_gtest_lib GTest::gtest)
    set(_gtest_main_lib GTest::gtest_main)
elseif(TARGET gtest AND TARGET gtest_main)
    set(_gtest_lib gtest)
    set(_gtest_main_lib gtest_main)
else()
    find_package(GTest CONFIG QUIET)
    if(GTest_FOUND AND TARGET GTest::gtest AND TARGET GTest::gtest_main)
        set(_gtest_lib GTest::gtest)
        set(_gtest_main_lib GTest::gtest_main)
    endif()
endif()
if(_gtest_lib STREQUAL "" OR _gtest_main_lib STREQUAL "")
    message(STATUS "GTest not found, skipping integration tests")
    return()
endif()

# Base test infrastructure
add_library(playhouse-test-base STATIC
    base_integration_test.cpp
    test_server_fixture.cpp
)

target_include_directories(playhouse-test-base PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}
)

target_link_libraries(playhouse-test-base PUBLIC
    playhouse-connector
    ${_gtest_lib}
)

# Platform-specific networking libraries for HTTP client
if(WIN32)
    target_link_libraries(playhouse-test-base PRIVATE ws2_32)
endif()

# Core integration tests
add_executable(c01_stage_creation_test
    core/c01_stage_creation_test.cpp
)
target_link_libraries(c01_stage_creation_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME C01_StageCreation COMMAND c01_stage_creation_test)

add_executable(c02_tcp_connection_test
    core/c02_tcp_connection_test.cpp
)
target_link_libraries(c02_tcp_connection_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME C02_TcpConnection COMMAND c02_tcp_connection_test)

add_executable(c03_authentication_success_test
    core/c03_authentication_success_test.cpp
)
target_link_libraries(c03_authentication_success_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME C03_AuthenticationSuccess COMMAND c03_authentication_success_test)

add_executable(c05_echo_request_response_test
    core/c05_echo_request_response_test.cpp
)
target_link_libraries(c05_echo_request_response_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME C05_EchoRequestResponse COMMAND c05_echo_request_response_test)

add_executable(c06_push_message_test
    core/c06_push_message_test.cpp
)
target_link_libraries(c06_push_message_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME C06_PushMessage COMMAND c06_push_message_test)

add_executable(c07_heartbeat_test
    core/c07_heartbeat_test.cpp
)
target_link_libraries(c07_heartbeat_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME C07_Heartbeat COMMAND c07_heartbeat_test)

add_executable(c08_disconnection_test
    core/c08_disconnection_test.cpp
)
target_link_libraries(c08_disconnection_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME C08_Disconnection COMMAND c08_disconnection_test)

add_executable(c09_authentication_failure_test
    core/c09_authentication_failure_test.cpp
)
target_link_libraries(c09_authentication_failure_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME C09_AuthenticationFailure COMMAND c09_authentication_failure_test)

add_executable(c10_request_timeout_test
    core/c10_request_timeout_test.cpp
)
target_link_libraries(c10_request_timeout_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME C10_RequestTimeout COMMAND c10_request_timeout_test)

add_executable(c11_error_response_test
    core/c11_error_response_test.cpp
)
target_link_libraries(c11_error_response_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME C11_ErrorResponse COMMAND c11_error_response_test)

# Advanced integration tests
add_executable(a01_websocket_connection_test
    advanced/a01_websocket_connection_test.cpp
)
target_link_libraries(a01_websocket_connection_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME A01_WebSocketConnection COMMAND a01_websocket_connection_test)

add_executable(a02_large_payload_test
    advanced/a02_large_payload_test.cpp
)
target_link_libraries(a02_large_payload_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME A02_LargePayload COMMAND a02_large_payload_test)

add_executable(a03_send_method_test
    advanced/a03_send_method_test.cpp
)
target_link_libraries(a03_send_method_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME A03_SendMethod COMMAND a03_send_method_test)

add_executable(a04_on_error_event_test
    advanced/a04_on_error_event_test.cpp
)
target_link_libraries(a04_on_error_event_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME A04_OnErrorEvent COMMAND a04_on_error_event_test)

add_executable(a05_multiple_connector_test
    advanced/a05_multiple_connector_test.cpp
)
target_link_libraries(a05_multiple_connector_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME A05_MultipleConnector COMMAND a05_multiple_connector_test)

add_executable(a06_edge_case_test
    advanced/a06_edge_case_test.cpp
)
target_link_libraries(a06_edge_case_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME A06_EdgeCase COMMAND a06_edge_case_test)

add_executable(a07_tls_connection_test
    advanced/a07_tls_connection_test.cpp
)
target_link_libraries(a07_tls_connection_test PRIVATE
    playhouse-test-base
    ${_gtest_main_lib}
)
add_test(NAME A07_TlsConnection COMMAND a07_tls_connection_test)
