#include "asio.h"
#include <iostream>


asio::Nconnector::Nconnector()
{
}

asio::Nconnector::~Nconnector() noexcept
{
}

void asio::Nconnector::start()
{
	// 1) create socket
	this->m_executor = asio::system::get_io_service().get_executor();
}

void asio::Nconnector::close() noexcept
{
}

void asio::Nconnector::request_connect(boost::asio::ip::tcp::endpoint _endpoint_connect)
{
#ifdef DEBUG_LOGGING
	std::cout << "[Nconnector] Requesting connection to "
			  << _endpoint_connect.address().to_string() << ":"
			  << _endpoint_connect.port() << std::endl;
#endif

	// 1) allock new socket
	auto psocket_new = this->process_create_socket();

	// check)
	assert(psocket_new);

	// check)
	if (!psocket_new)
		throw std::bad_alloc();

	// 1) set socket state ESOCKET_STATUE::ESTABLISHED
	{
		// - desiged state
		ESOCKET_STATUE socket_state_old = ESOCKET_STATUE::NONE;

		// - change state
		auto changed = psocket_new->m_socket_state.compare_exchange_weak(socket_state_old, ESOCKET_STATUE::SYN);

		// check)
		assert(changed == true);

		// return)
		if (changed == false)
			throw std::runtime_error("socket aleady connected or tring connectiong");
	}

	try
	{
		// 2) register socket first
		this->process_register_socket(psocket_new);

		// statistics)
		++Nstatistics::statistics_connect_try;

#ifdef DEBUG_LOGGING
		std::cout << "[Nconnector] Socket registered, initiating async_connect..." << std::endl;
#endif

		// 3) request connect
		psocket_new->native_handle().async_connect(_endpoint_connect, [=,this](const boost::system::error_code& _error)
			{
				this->process_connect_request_complete(psocket_new, _error);
			});
	}
	catch (...)
	{
		std::cerr << "[Nconnector] Exception during connection setup" << std::endl;

		// - rollback
		this->process_unregister_socket(psocket_new);

		// - rollback (set socket state ESOCKET_STATUE::NONE)
		psocket_new->m_socket_state.exchange(ESOCKET_STATUE::NONE);

		// reraise)
		throw;
	}
}

void asio::Nconnector::process_connect_request_complete(std::shared_ptr<Isocket_tcp> _socket, const boost::system::error_code& _error)
{
	try
	{
		// check) 실패했을 경우 등록해제하고 소켓을 닫고 끝낸다.
		if (_error)
		{
#ifdef DEBUG_LOGGING
			std::cerr << "[Nconnector] Connection failed: " << _error.message()
					  << " (code: " << _error.value() << ")" << std::endl;
#endif
			throw std::runtime_error("connection failure");
		}

#ifdef DEBUG_LOGGING
		std::cout << "[Nconnector] Connection established successfully" << std::endl;
#endif

		// 1) process connect socket
		_socket->process_connect_complate();

		// statistics)
		++Nstatistics::statistics_connect_success;
		++Nstatistics::statistics_connect_keep;

#ifdef DEBUG_LOGGING
		std::cout << "[Nconnector] Socket connection completed" << std::endl;
#endif
	}
	catch (const std::exception& e)
	{
#ifdef DEBUG_LOGGING
		std::cerr << "[Nconnector] Exception in process_connect_request_complete: "
				  << e.what() << std::endl;
#endif

		// - rollback
		_socket->process_connective_closesocket();

		// - rollback (set socket state ESOCKET_STATUE::NONE)
		_socket->m_socket_state.exchange(ESOCKET_STATUE::NONE);
	}
	catch (...)
	{
#ifdef DEBUG_LOGGING
		std::cerr << "[Nconnector] Unknown exception in process_connect_request_complete" << std::endl;
#endif

		// - rollback
		_socket->process_connective_closesocket();

		// - rollback (set socket state ESOCKET_STATUE::NONE)
		_socket->m_socket_state.exchange(ESOCKET_STATUE::NONE);
	}
}



