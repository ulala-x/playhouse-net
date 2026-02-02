#ifndef PLAYHOUSE_ASIO_INCLUDE_HPP
#define PLAYHOUSE_ASIO_INCLUDE_HPP

// This header provides a unified way to include ASIO headers
// Supports both standalone ASIO and Boost.Asio

#if defined(ASIO_STANDALONE)
    // Standalone ASIO (preferred)
    #include <asio.hpp>
    #include <asio/awaitable.hpp>
    #include <asio/co_spawn.hpp>
    #include <asio/detached.hpp>
    #include <asio/use_awaitable.hpp>
    #include <asio/experimental/promise.hpp>
#elif defined(BOOST_ASIO_HPP)
    // Boost.Asio
    #include <boost/asio.hpp>
    #include <boost/asio/awaitable.hpp>
    #include <boost/asio/co_spawn.hpp>
    #include <boost/asio/detached.hpp>
    #include <boost/asio/use_awaitable.hpp>
    namespace asio = boost::asio;
#else
    // Default to standalone ASIO
    #ifndef ASIO_STANDALONE
        #define ASIO_STANDALONE
    #endif
    #include <asio.hpp>
    #include <asio/awaitable.hpp>
    #include <asio/co_spawn.hpp>
    #include <asio/detached.hpp>
    #include <asio/use_awaitable.hpp>
    #include <asio/experimental/promise.hpp>
#endif

#endif // PLAYHOUSE_ASIO_INCLUDE_HPP
