# Voice Battle online server (tools/voice-battle-server). Build from the repo root:
#   docker build -t voice-battle-server .
#   docker run -d --name vb -p 7777:7777 --restart unless-stopped voice-battle-server
# Public server with Steam login (key never goes in the image or in git):
#   docker run -d --name vb -p 7777:7777 -e VB_AUTH=steam -e VB_STEAM_WEBAPI_KEY=... -e VB_STEAM_APP_ID=480 voice-battle-server
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json ./
COPY src/ src/
COPY tools/voice-battle-server/ tools/voice-battle-server/
RUN dotnet publish tools/voice-battle-server -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app ./
# Only what the server reads: limits, rules, modes and licensed packs (no audio, no lyrics).
COPY data/server.json data/moves.json data/battle.json data/modes.json /app/data/
COPY data/Packs/ /app/data/Packs/
USER app
EXPOSE 7777
ENTRYPOINT ["dotnet", "voice-battle-server.dll", "--data", "/app/data"]
