using System.Threading.Tasks;

namespace Channels
{
    public class NullChannel : WritableChannel
    {
        internal NullChannel(IBufferPool pool)
            : base(pool)
        {
        }

        /// <summary>
        /// Commits all outstanding written data to the underlying <see cref="IWritableChannel"/> so they can be read
        /// and seals the <see cref="WritableBuffer"/> so no more data can be committed.
        /// </summary>
        /// <remarks>
        /// While an on-going conncurent read may pick up the data, <see cref="FlushAsync"/> should be called to signal the reader.
        /// </remarks>
        public override void Commit()
        {
            _channel.Commit();

            var result = _channel.ReadAsync().GetAwaiter().GetResult();
            var buffer = result.Buffer;
            _channel.AdvanceReader(buffer.Start, buffer.End);

            if (result.IsCompleted)
            {
                _channel.CompleteReader();
            }
        }

        /// <summary>
        /// Signals the <see cref="IReadableChannel"/> data is available.
        /// Will <see cref="Commit"/> if necessary.
        /// </summary>
        /// <returns>A task that completes when the data is fully flushed.</returns>
        public override async Task FlushAsync()
        {
            _channel.Commit();

            var result = await _channel.ReadAsync();
            var buffer = result.Buffer;
            _channel.AdvanceReader(buffer.Start, buffer.End);

            if (result.IsCompleted)
            {
                _channel.CompleteReader();
            }
        }
    }
}
