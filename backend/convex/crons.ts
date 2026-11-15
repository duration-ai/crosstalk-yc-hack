import { cronJobs } from "convex/server";
import { internal } from "./_generated/api";

const crons = cronJobs();

crons.interval("sweep timed-out world presence", { seconds: 5 }, internal.world.sweepStale, {});

export default crons;
