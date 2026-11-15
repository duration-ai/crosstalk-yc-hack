using System.Threading;
using System.Threading.Tasks;
using Dogpatch.Race;

namespace Dogpatch.Operator
{
    // What the caller said into the payphone: words, a recording, or both. The contract has two
    // endpoints for it, POST /api/operator (text) and POST /api/operator/audio (raw recording).
    public sealed class OperatorCall
    {
        public string PlayerId;
        public string RoomId;
        public string Text;
        public byte[] Audio;
        public string AudioMime;
    }

    // The Operator on the other end of every payphone. She hears a call and answers with exactly
    // one world change and a short line for her pink bar, and the change is already in the room's
    // event log when she answers (OperatorResponse.event), so every client applies it from its poll.
    // LocalOperator stands in on the device; the voice Operator on the backend implements the same
    // interface over HTTP.
    public interface IOperator
    {
        Task<OperatorResponse> CallAsync(OperatorCall call, CancellationToken cancel = default);
    }
}
