using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dogpatch.Backend;
using Dogpatch.Race;
using Dogpatch.WorldChanges;

namespace Dogpatch.Operator
{
    // A stand-in Operator that runs on the device. Words she recognises pick the world change, the
    // same keyword idea as the backend's keyword provider; a call with no words (the phone has no
    // microphone yet, and she cannot hear audio) takes the next change in the catalog's roulette, so
    // each call cycles through them. Each call that gets a change spends one of the caller's asks
    // through the race session; with none left she turns the call down. She fires the change through
    // the race session and answers with its line.
    public sealed class LocalOperator : IOperator
    {
        readonly IRaceSession session;
        readonly WorldChangeCatalog catalog;
        int next;

        public LocalOperator(IRaceSession session, WorldChangeCatalog catalog)
        {
            this.session = session;
            this.catalog = catalog;
        }

        public WorldChangeCommand Peek()
        {
            var roulette = catalog.Roulette();
            return roulette[next % roulette.Count];
        }

        public async Task<OperatorResponse> CallAsync(OperatorCall call, CancellationToken cancel = default)
        {
            try
            {
                await session.SpendAskAsync(call.PlayerId, call.RoomId);
            }
            catch (BackendException e) when (e.Code == BackendErrorCodes.Conflict)
            {
                return new OperatorResponse
                {
                    line = CallController.NoAsksLine,
                    provider = "local",
                    outcome = "refused",
                    refusal = new OperatorRefusal { reason = "no_asks", message = e.Message },
                };
            }
            var command = catalog.Match(call.Text);
            if (command == null)
            {
                command = Peek();
                next++;
            }
            command = WorldChangeCatalog.Command(command.type, command.@params);

            if (command.type == "swap")
            {
                var poll = await session.PollAsync(call.RoomId, null);
                var racers = poll.racers.Where(r => !r.left).OrderBy(r => r.place).ToList();
                int me = racers.FindIndex(r => r.playerId == call.PlayerId);
                var target = me > 0 ? racers[me - 1] : racers.Count > 1 ? racers[1] : null;
                if (target == null) command = WorldChangeCatalog.Command("ramp", new WorldChangeParams { count = 3 });
                else command.@params.targetPlayerId = target.playerId;
            }
            cancel.ThrowIfCancellationRequested();

            var fired = await session.FireEventAsync(call.PlayerId, call.RoomId, command);
            return new OperatorResponse
            {
                line = catalog.Line(command),
                command = new WorldChangeCommand { type = fired.@event.type, @params = fired.@event.@params },
                @event = fired.@event,
                provider = "local",
            };
        }
    }
}
