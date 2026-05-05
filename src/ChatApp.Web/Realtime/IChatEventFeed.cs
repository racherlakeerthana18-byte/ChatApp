using ChatApp.Application.Contracts;

namespace ChatApp.Web.Realtime;

public interface IChatEventFeed
{
    IDisposable Subscribe(Func<ChatEvent, Task> handler);
}
