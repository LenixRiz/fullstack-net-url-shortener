using System.Threading.Channels;
using UrlShortener.Api.Domain;

namespace UrlShortener.Api.Services;

public class ClickChannel
{
    private readonly Channel<ClickEvent> _channel;

    public ClickChannel()
    {
        var options = new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        };
        _channel = Channel.CreateBounded<ClickEvent>(options);
    }

    public ChannelWriter<ClickEvent> Writer => _channel.Writer;
    public ChannelReader<ClickEvent> Reader => _channel.Reader;
}