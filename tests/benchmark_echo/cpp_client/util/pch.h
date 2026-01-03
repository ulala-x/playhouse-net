// Precompiled header for util
#pragma once

#include <cstdint>
#include <cstddef>
#include <cstring>
#include <string>
#include <chrono>
#include <thread>
#include <atomic>
#include <memory>
#include <vector>
#include <map>
#include <set>
#include <algorithm>
#include <functional>
#include <iostream>
#include <sstream>
#include <fstream>

// Platform-specific headers
#ifdef __linux__
#include <unistd.h>
#include <sys/time.h>
#include <sys/types.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <arpa/inet.h>
#include <fcntl.h>
#include <errno.h>
#endif
