--[[
	ROUND SYSTEM (intermission + round timer)
	Lobby countdown -> everyone goes to the arena -> round timer counts down ->
	survivors get a Win -> everyone goes back to the lobby -> repeat forever.
	The current message (like "Intermission: 10") is stored in a StringValue
	called "Status" in ReplicatedStorage, so GUIs can show it.

	HOW TO USE:
	1. In Workspace make two Anchored Parts named exactly: LobbySpawn and ArenaSpawn
	   (players get teleported on top of them).
	2. Put this Script in ServerScriptService.
	3. To show the timer on screen, use the "status-label" LocalScript.
]]

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

-- SETTINGS
local INTERMISSION_TIME = 15 -- seconds in the lobby
local ROUND_TIME = 60 -- seconds the round lasts
local MIN_PLAYERS = 1 -- players needed to start (set to 2 for real games)
local END_IF_ONE_LEFT = true -- end early when only 1 player is left (if 2+ started)

local lobbySpawn = workspace:WaitForChild("LobbySpawn")
local arenaSpawn = workspace:WaitForChild("ArenaSpawn")

-- The Status message everyone can read
local status = ReplicatedStorage:FindFirstChild("Status")
if not status then
	status = Instance.new("StringValue")
	status.Name = "Status"
	status.Parent = ReplicatedStorage
end

local function getWins(player)
	local leaderstats = player:FindFirstChild("leaderstats")
	if not leaderstats then
		leaderstats = Instance.new("Folder")
		leaderstats.Name = "leaderstats"
		leaderstats.Parent = player
	end
	local wins = leaderstats:FindFirstChild("Wins")
	if not wins then
		wins = Instance.new("IntValue")
		wins.Name = "Wins"
		wins.Parent = leaderstats
	end
	return wins
end

Players.PlayerAdded:Connect(getWins)
for _, player in Players:GetPlayers() do
	getWins(player)
end

local function teleportTo(character, spawnPart)
	-- Spread people out a little so they don't land on each other
	local offset = Vector3.new(math.random(-4, 4), spawnPart.Size.Y / 2 + 4, math.random(-4, 4))
	character:PivotTo(CFrame.new(spawnPart.Position + offset))
end

local function isAlive(character)
	local humanoid = character and character:FindFirstChildOfClass("Humanoid")
	return humanoid ~= nil and humanoid.Health > 0
end

while true do
	-- 1) Wait for enough players
	while #Players:GetPlayers() < MIN_PLAYERS do
		status.Value = "Waiting for players"
		task.wait(1)
	end

	-- 2) Intermission countdown
	for secondsLeft = INTERMISSION_TIME, 1, -1 do
		status.Value = "Intermission: " .. secondsLeft
		task.wait(1)
	end

	-- 3) Send everyone who is alive to the arena.
	-- We remember the exact character each player had, so if they die and
	-- respawn in the lobby, they don't count as still in the round.
	local roundCharacters = {} -- [player] = character
	local startedWith = 0
	for _, player in Players:GetPlayers() do
		local character = player.Character
		if isAlive(character) then
			roundCharacters[player] = character
			startedWith += 1
			teleportTo(character, arenaSpawn)
		end
	end

	local function getSurvivors()
		local survivors = {}
		for player, character in roundCharacters do
			if player.Parent == Players and player.Character == character and isAlive(character) then
				table.insert(survivors, player)
			end
		end
		return survivors
	end

	-- 4) Round countdown
	for secondsLeft = ROUND_TIME, 1, -1 do
		local survivors = getSurvivors()
		if #survivors == 0 then
			break
		end
		if END_IF_ONE_LEFT and startedWith >= 2 and #survivors == 1 then
			break
		end
		status.Value = "Round: " .. secondsLeft .. "s left (" .. #survivors .. " alive)"
		task.wait(1)
	end

	-- 5) Give survivors a Win and send them back
	local survivors = getSurvivors()
	if #survivors == 0 then
		status.Value = "Nobody survived!"
	else
		local names = {}
		for _, player in survivors do
			getWins(player).Value += 1
			table.insert(names, player.DisplayName)
			teleportTo(player.Character, lobbySpawn)
		end
		status.Value = "Winners: " .. table.concat(names, ", ")
	end

	task.wait(4)
end
