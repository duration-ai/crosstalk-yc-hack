using System.Threading.Tasks;

namespace Dogpatch.Operator
{
    public struct Heard
    {
        public string Text;
        public int SttMs;
    }

    // Speech for the payphone. CallController drives it from the Call plate: Open when the call
    // plate slides in, BeginTalk on press, FinishAsync on release (the final transcript), Cancel
    // for a tap too short to be a call, Close when the call ends.
    public interface IVoiceInput
    {
        string Partial { get; }

        void Open();

        void BeginTalk();

        Task<Heard> FinishAsync();

        void Cancel();

        void Close();
    }
}
