namespace UrlShortener.Api.Services;

public interface IIdGenerator
{
    long CreateId();
}

public class SnowflakeIdGenerator : IIdGenerator
{
    private const long CustomEpoch = 1767225600000L;
    private const int SequenceBits = 12;
    private const int NodeIdBits = 10;

    private const long MaxSequence = (1L << SequenceBits) - 1;
    private const int NodeIdShift = SequenceBits;
    private const int TimestampShift = SequenceBits + NodeIdBits;

    private readonly long _nodeId;
    private readonly object _lock = new();
    private long _lastTimestamp = -1L;
    private long _sequence = 0L;

    public SnowflakeIdGenerator(long nodeId = 1)
    {
        _nodeId = nodeId;
    }

    public long CreateId()
    {
        lock (_lock)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (timestamp < _lastTimestamp)
            {
                throw new InvalidOperationException("Clock moved backwards. Refusing to generate ID");
            }

            if (timestamp == _lastTimestamp)
            {
                _sequence = (_sequence + 1) & MaxSequence;
                if (_sequence == 0)
                {
                    while (timestamp <= _lastTimestamp)
                    {
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    }
                }
            }
            else
            {
                _sequence = 0;
            }

            _lastTimestamp = timestamp;

            return ((timestamp - CustomEpoch) << TimestampShift)
                | (_nodeId << NodeIdShift)
                | _sequence;
        }
    }
}