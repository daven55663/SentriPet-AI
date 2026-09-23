# Example usage script for SentriPet.
# Any language works: the pet only needs this JSON printed to stdout.
import datetime
import json

now = datetime.datetime.now().astimezone()
five_hours = (now + datetime.timedelta(hours=3, minutes=12)).isoformat()

print(json.dumps({
    "plan": "Demo",
    "note": "This is a demo plugin",
    "meters": [
        {"label": "5h", "used": 37, "resetsAt": five_hours, "windowMinutes": 300},
        {"label": "week", "used": 12, "windowMinutes": 10080},
    ],
}))
