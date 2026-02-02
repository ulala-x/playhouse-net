#nullable enable

namespace PlayHouse.Core.Shared.TaskPool;

/// <summary>
/// TaskPool에서 실행할 수 있는 작업 항목을 나타내는 인터페이스입니다.
/// </summary>
public interface ITaskPoolWorkItem
{
    /// <summary>
    /// 작업을 비동기로 실행합니다.
    /// </summary>
    /// <returns>작업이 완료되면 완료되는 Task</returns>
    Task ExecuteAsync();
}
