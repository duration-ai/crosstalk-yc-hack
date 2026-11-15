import { DECISION } from "./avatarData";
import { postDecision, type DecisionQuestion } from "./jev";

export class DecisionUnavailable extends Error {}

export type DecisionAttempt = { model: string; ok: boolean; ms: number; error?: string };

export type HedgedDecision<T> = {
  value: T;
  provider: string;
  latencyMs: number;
  hedged: boolean;
  attempts: DecisionAttempt[];
};

type Config = Pick<typeof DECISION, "primaryModel" | "backupModel" | "hedgeAfterMs" | "requestTimeoutMs">;

/**
 * Asks the primary decision model and, if it has not answered within hedgeAfterMs or fails first,
 * the backup too; the first reply that parses wins and the other request is aborted.
 */
export function hedgedDecision<T>(
  apiKey: string,
  input: unknown,
  questions: DecisionQuestion[],
  parse: (answers: unknown[]) => T,
  config: Config = DECISION,
): Promise<HedgedDecision<T>> {
  const startedAt = Date.now();
  const attempts: DecisionAttempt[] = [];
  const controllers: AbortController[] = [];
  return new Promise((resolve, reject) => {
    let settled = false;
    let pending = 0;
    let backupStarted = false;
    const settle = () => {
      settled = true;
      clearTimeout(hedgeTimer);
      for (const controller of controllers) controller.abort();
    };
    const launch = (model: string) => {
      pending++;
      const controller = new AbortController();
      controllers.push(controller);
      const timer = setTimeout(() => controller.abort(), config.requestTimeoutMs);
      const launchedAt = Date.now();
      postDecision(apiKey, model, input, questions, controller.signal)
        .then(({ answers }) => parse(answers))
        .then(
          (value) => {
            attempts.push({ model, ok: true, ms: Date.now() - launchedAt });
            if (settled) return;
            settle();
            resolve({ value, provider: model, latencyMs: Date.now() - startedAt, hedged: backupStarted, attempts: [...attempts] });
          },
          (error) => {
            const message = controller.signal.aborted && !settled ? `timeout after ${config.requestTimeoutMs} ms` : String(error?.message ?? error);
            attempts.push({ model, ok: false, ms: Date.now() - launchedAt, error: message });
            if (settled) return;
            if (model === config.primaryModel) startBackup();
            if (--pending === 0) {
              settle();
              reject(new DecisionUnavailable(attempts.map((attempt) => `${attempt.model}: ${attempt.error}`).join("; ")));
            }
          },
        )
        .finally(() => clearTimeout(timer));
    };
    const startBackup = () => {
      if (backupStarted || settled) return;
      backupStarted = true;
      launch(config.backupModel);
    };
    const hedgeTimer = setTimeout(startBackup, config.hedgeAfterMs);
    launch(config.primaryModel);
  });
}
