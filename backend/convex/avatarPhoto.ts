"use node";

import { v } from "convex/values";
import sharp from "sharp";
import { internal } from "./_generated/api";
import { internalAction } from "./_generated/server";
import type { toAvatar as avatarRow } from "./avatars";
import { DECISION } from "./avatarData";
import { DecisionUnavailable, hedgedDecision } from "./avatarDecision";
import { buildQuestions, PROMPT, readAnswers, RetakeRequired, toAvatar, type RawAnswer } from "./avatarModel";

type Summary = {
  provider: string;
  latencyMs: number;
  hedged: boolean;
  realPerson: number;
  costume: string;
  costumeProbability: number;
};

export type AnalyzeResult =
  | { kind: "ok"; avatar: ReturnType<typeof avatarRow>; decision: Summary; answers: RawAnswer[] }
  | { kind: "retake"; message: string; decision: Summary }
  | { kind: "invalid"; message: string }
  | { kind: "unavailable"; message: string };

export const analyze = internalAction({
  args: { playerId: v.string(), photo: v.bytes() },
  handler: async (ctx, { playerId, photo }): Promise<AnalyzeResult> => {
    const startedAt = Date.now();
    let jpeg: Buffer;
    try {
      jpeg = await sharp(Buffer.from(photo))
        .rotate()
        .resize(DECISION.imageMaxSide, DECISION.imageMaxSide, { fit: "inside", withoutEnlargement: true })
        .jpeg({ quality: DECISION.imageQuality })
        .toBuffer();
    } catch (error) {
      return { kind: "invalid" as const, message: `Could not read the image: ${String(error)}` };
    }
    const resizeMs = Date.now() - startedAt;
    const apiKey = process.env.AI_GATEWAY_API_KEY;
    if (!apiKey) return { kind: "unavailable" as const, message: "AI_GATEWAY_API_KEY is not set" };

    const input = [
      {
        role: "user",
        content: [
          { type: "input_text", text: PROMPT },
          { type: "input_image", image_url: `data:image/jpeg;base64,${jpeg.toString("base64")}`, detail: DECISION.imageDetail },
        ],
      },
    ];
    let decision;
    try {
      decision = await hedgedDecision(apiKey, input, buildQuestions(), (answers) => ({
        raw: answers as RawAnswer[],
        reading: readAnswers(answers as RawAnswer[]),
      }));
    } catch (error) {
      if (!(error instanceof DecisionUnavailable)) throw error;
      console.log(JSON.stringify({ event: "avatar.decision", playerId, ok: false, error: error.message, ms: Date.now() - startedAt }));
      return { kind: "unavailable" as const, message: error.message };
    }
    const { reading, raw } = decision.value;
    const summary: Summary = {
      provider: decision.provider,
      latencyMs: decision.latencyMs,
      hedged: decision.hedged,
      realPerson: reading.realPerson,
      costume: reading.costume.choice,
      costumeProbability: Math.max(0, ...Object.entries(reading.costume.probabilities).filter(([k]) => k !== "none").map(([, p]) => p)),
    };
    console.log(
      JSON.stringify({
        event: "avatar.decision",
        playerId,
        ok: true,
        ...summary,
        resizeMs,
        bytesIn: photo.byteLength,
        bytesSent: jpeg.byteLength,
        attempts: decision.attempts,
      }),
    );
    try {
      const params = toAvatar(reading);
      const avatar: ReturnType<typeof avatarRow> = await ctx.runMutation(internal.avatars.save, { playerId, params, decision: summary });
      return { kind: "ok" as const, avatar, decision: summary, answers: raw };
    } catch (error) {
      if (!(error instanceof RetakeRequired)) throw error;
      return { kind: "retake" as const, message: error.message, decision: summary };
    }
  },
});
