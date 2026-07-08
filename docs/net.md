# Net — HTTP requests

```mako
using Net;

main() {
    res = Net.get("https://api.example.com/data");
    if Net.ok(res) {
        data = Net.json(res);          # decode the body as JSON directly
        print data["field"];
    } else {
        print "request failed: {Net.status(res)} {Net.error(res)}";
    }
}
```

A response is always a plain dict — `{"status": 200, "ok": true, "body":
"...", "error": ""}` — so you can also index it directly (`res["status"]`).
The `Net.*` helpers below just make that less verbose.

## Requests

| Function | Description |
|---|---|
| `get(url, headers=none)` | GET request |
| `post(url, body, headers=none)` | POST — `body` is sent as the request body (e.g. a JSON string) |
| `put(url, body, headers=none)` | PUT |
| `delete(url, headers=none)` | DELETE |

`headers` is an optional dict: `{"Authorization": "Bearer ..."}`.

Requests never throw — a failed connection, timeout, or DNS error comes back
as a response with `status: 0` and a message in `error`, so you always
handle it with `Net.ok(res)` rather than `try`/`catch`.

## Response helpers

| Function | Description |
|---|---|
| `ok(res)` | `true` for 2xx status codes |
| `status(res)` | Numeric HTTP status (`0` if the request never reached the server) |
| `body(res)` | Raw response body string |
| `error(res)` | Connection/timeout error message, `""` on success |
| `json(res)` | `body(res)` decoded as JSON — `none` if it isn't valid JSON |

## JSON (works without `using Net;`)

```mako
s = json_encode({"name": "Robin", "score": 42, "tags": [1, 2, 3]});
d = json_decode(s);
print d["name"];
```

`json_encode` handles numbers, strings, booleans, `none` (→ `null`), lists,
and dicts. `json_decode` throws a catchable error on malformed input.

## Utility

| Function | Description |
|---|---|
| `url_encode(s)` | Percent-encode a string for use in a URL |
| `url_decode(s)` | Reverse it |

## Example — posting JSON

```mako
using Net;

main() {
    payload = json_encode({"title": "hello", "done": false});
    res = Net.post("https://api.example.com/tasks", payload,
                    {"Content-Type": "application/json"});
    if Net.ok(res) {
        created = Net.json(res);
        print "created id {created["id"]}";
    }
}
```
