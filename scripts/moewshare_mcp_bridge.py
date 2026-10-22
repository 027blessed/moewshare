#!/usr/bin/env python3
"""
Moewshare MCP Stdio Bridge
Translates stdio JSON-RPC MCP messages to Moewshare Editor's embedded HTTP MCP server.
"""

import sys
import json
import urllib.request
import urllib.error

DEFAULT_PORT = 45124

def main():
    port = DEFAULT_PORT
    if len(sys.argv) > 1 and sys.argv[1].isdigit():
        port = int(sys.argv[1])

    url = f"http://127.0.0.1:{port}/mcp"

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            req_data = line.encode('utf-8')
            http_req = urllib.request.Request(
                url,
                data=req_data,
                headers={"Content-Type": "application/json"}
            )
            with urllib.request.urlopen(http_req, timeout=30) as resp:
                resp_bytes = resp.read()
                sys.stdout.buffer.write(resp_bytes)
                sys.stdout.buffer.write(b"\n")
                sys.stdout.flush()
        except urllib.error.URLError as e:
            try:
                msg = json.loads(line)
                req_id = msg.get("id")
            except Exception:
                req_id = None
            err_resp = {
                "jsonrpc": "2.0",
                "id": req_id,
                "error": {
                    "code": -32000,
                    "message": f"Не удалось подключиться к Moewshare Editor на {url}. Убедитесь, что редактор запущен и MCP сервер включен в Настройках!"
                }
            }
            sys.stdout.write(json.dumps(err_resp) + "\n")
            sys.stdout.flush()
        except Exception as e:
            sys.stderr.write(f"Bridge error: {e}\n")
            sys.stderr.flush()

if __name__ == "__main__":
    main()
