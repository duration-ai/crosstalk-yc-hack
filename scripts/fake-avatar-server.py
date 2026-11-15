# Fake avatar server for scripts/avatar-ui-shots.sh: GET /api/avatar answers {"avatar": null};
# POST /api/avatar/photo answers 422 RETAKE, then 503 DECISION_UNAVAILABLE, then (after 4 s) 200
# with the avatar from the JSON file given as the first argument. Second argument: port.
import json, sys, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

avatar = json.load(open(sys.argv[1]))["avatar"]
count = {"n": 0}

class H(BaseHTTPRequestHandler):
    def reply(self, code, body):
        data = json.dumps(body).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        self.reply(200, {"avatar": None})

    def do_POST(self):
        q = parse_qs(urlparse(self.path).query)
        body = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        count["n"] += 1
        n = count["n"]
        print(f"POST #{n} {urlparse(self.path).path} playerId={q.get('playerId')} type={self.headers.get('Content-Type')} bytes={len(body)} jpeg={body[:2].hex()}", flush=True)
        if n == 1:
            return self.reply(422, {"error": {"code": "RETAKE", "message": "No person in the photo"}, "realPerson": 0.04})
        if n == 2:
            return self.reply(503, {"error": {"code": "DECISION_UNAVAILABLE", "message": "No decision in time"}})
        time.sleep(4)
        a = dict(avatar, playerId=q.get("playerId", [""])[0])
        self.reply(200, {"avatar": a, "decision": {"provider": "fake", "latencyMs": 4000, "hedged": False, "realPerson": 0.98, "costume": "none", "costumeProbability": 0.02}})

ThreadingHTTPServer(("127.0.0.1", int(sys.argv[2])), H).serve_forever()
