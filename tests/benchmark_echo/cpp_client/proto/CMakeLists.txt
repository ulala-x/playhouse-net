# Proto CMakeLists.txt - Use manually generated protobuf code

# Create proto library from manually generated files
add_library(echo_proto STATIC
    echo.pb.cc
    echo.pb.h
)

# Include directories
target_include_directories(echo_proto PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}
)

# Set C++ standard
target_compile_features(echo_proto PUBLIC cxx_std_20)
