# Simulator External Dependencies

## Principle

All external API dependencies are abstracted behind interfaces with config-driven base URLs. In production, URLs point to real services. In dev/staging/E2E testing, URLs point to the simulator. The same application code runs in all environments — only the config-driven base URL changes per environment.

## Current Instances

| External Service | Config Key | Production URL | Dev/Staging URL |
|---|---|---|---|
| Google Calendar API | `GoogleCalendar:CalendarApiBaseUrl` | `https://www.googleapis.com` | `https://localhost:7199` (simulator) |
| Google OAuth | `GoogleCalendar:AuthBaseUrl` | `https://accounts.google.com` | `https://localhost:7199` (simulator) |
| Google Calendar Watch | `GoogleCalendar:CalendarApiBaseUrl` | `https://www.googleapis.com` | `https://localhost:7199` (simulator) |
| Open-Meteo Weather API | `Weather:BaseUrl` | `https://api.open-meteo.com` | `https://localhost:7199` (simulator) |
| Nominatim Geocoding | `Geocoding:BaseUrl` | `https://nominatim.openstreetmap.org` | `https://localhost:7199` (simulator) |
| ip-api.com IP Geolocation | `Location:IpApiBaseUrl` | `http://ip-api.com` | `https://localhost:7199` (simulator) |

## Pattern

The simulator implements each external API's response format, for example:
- Nominatim `/search?q=...` response shape
- Open-Meteo `/v1/forecast` response shape

This means production and test code follow identical code paths. No test-only branches or conditional logic in the application.

## Simulator Backdoor Endpoints

For E2E test isolation, the simulator exposes `POST/DELETE /api/simulator/backdoor/*` endpoints that let tests inject and clear data:

| Endpoint | Purpose |
|---|---|
| `POST /api/simulator/backdoor/weather` | Seed weather data by lat/lon |
| `DELETE /api/simulator/backdoor/weather?latitude=X&longitude=Y` | Clear weather data |
| `POST /api/simulator/backdoor/location` | Seed geocoding result by place name |
| `DELETE /api/simulator/backdoor/location?placeName=X` | Clear geocoding result |
| `POST /api/simulator/configure` | Configure user templates |
| `POST /api/simulator/backdoor/events` | Seed calendar events |
| `GET /api/simulator/backdoor/webhooks` | Query registered watch channels |
| `DELETE /api/simulator/backdoor/webhooks` | Clear registered watch channels |

## Seeded Locations

The simulator seeds the following locations on startup for manual testing. Enter any of these place names on the Settings page to save a location and see weather data.

| Place Name | Latitude | Longitude |
|---|---|---|
| Edinburgh, Scotland | 55.9533 | -3.1883 |
| London, England | 51.5074 | -0.1278 |
| Dublin, Ireland | 53.3498 | -6.2603 |
| New York, USA | 40.7128 | -74.0060 |
| Tokyo, Japan | 35.6762 | 139.6503 |
| Sydney, Australia | -33.8688 | 151.2093 |

The geocoding controller uses fuzzy matching (`ILIKE %q%`), so partial names like "Edinburgh" or "Tokyo" will also work.

## Adding a New External Dependency

1. Create an interface in `FamilyHQ.Core`
2. Implement against the real API in `FamilyHQ.Services`
3. Register with `AddHttpClient` using a config-driven base URL in `Program.cs`
4. Add a simulator controller that mimics the external API's response format
5. Add a backdoor controller for E2E test data injection
6. Set dev/staging config to point to the simulator
