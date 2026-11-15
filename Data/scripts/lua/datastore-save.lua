--[[
	SAVE LEADERSTATS WITH DATASTORE
	Saves every number in a player's leaderstats (Coins, Stage, Wins, Kills and more)
	when they leave, and loads it back when they join again.

	HOW TO USE:
	1. Publish your game (File > Publish to Roblox).
	2. In Studio: Home > Game Settings > Security > turn ON "Enable Studio Access to API Services".
	3. Put this Script in ServerScriptService.
	It works with any other script that makes leaderstats (coins, checkpoints, kills and more).

	SAFETY FEATURES:
	- pcall: if Roblox's servers have a hiccup, the game doesn't crash.
	- Retries: it tries again a few times before giving up.
	- If loading FAILED, it will NOT save (so it never overwrites good data with zeros).
	- BindToClose: saves everyone when the server shuts down.
	- Autosave every couple of minutes.
]]

local Players = game:GetService("Players")
local DataStoreService = game:GetService("DataStoreService")

-- SETTINGS
local STORE_NAME = "PlayerStats_v1" -- change the name to start everyone fresh
local STARTING_STATS = { -- stats every player gets (add more like: Wins = 0)
	Coins = 0,
}
local MAX_TRIES = 3 -- how many times to try loading/saving
local AUTOSAVE_SECONDS = 120 -- autosave every 2 minutes

-- Getting the DataStore can fail in an unpublished place, so we use pcall
local gotStore, store = pcall(function()
	return DataStoreService:GetDataStore(STORE_NAME)
end)
if not gotStore then
	warn("DataStore not available (publish the game and turn on API access): " .. tostring(store))
	store = nil
end

local loadedOk = {} -- [player] = true when their data loaded fine

local function keyFor(player)
	return "Player_" .. player.UserId
end

-- Runs a function, and if it errors, waits and tries again
local function tryAFewTimes(job)
	local lastError = nil
	for attempt = 1, MAX_TRIES do
		local ok, result = pcall(job)
		if ok then
			return true, result
		end
		lastError = result
		warn("DataStore try " .. attempt .. " failed: " .. tostring(result))
		if attempt < MAX_TRIES then
			task.wait(attempt * 2)
		end
	end
	return false, lastError
end

local function getLeaderstats(player)
	local leaderstats = player:FindFirstChild("leaderstats")
	if not leaderstats then
		leaderstats = Instance.new("Folder")
		leaderstats.Name = "leaderstats"
		leaderstats.Parent = player
	end
	return leaderstats
end

local function loadPlayer(player)
	local leaderstats = getLeaderstats(player)

	-- Make the starting stats (if another script didn't already)
	for statName, startValue in STARTING_STATS do
		if not leaderstats:FindFirstChild(statName) then
			local stat = Instance.new("IntValue")
			stat.Name = statName
			stat.Value = startValue
			stat.Parent = leaderstats
		end
	end

	if not store then
		return
	end

	local ok, savedData = tryAFewTimes(function()
		return store:GetAsync(keyFor(player))
	end)

	if not player.Parent then
		return -- they left while we were loading
	end

	if not ok then
		warn("Could not load data for " .. player.Name .. ". Their progress will NOT be saved this time.")
		return
	end

	if type(savedData) == "table" then
		for statName, savedValue in savedData do
			if type(statName) == "string" and type(savedValue) == "number" then
				local stat = leaderstats:FindFirstChild(statName)
				if not stat then
					stat = Instance.new("IntValue")
					stat.Name = statName
					stat.Parent = leaderstats
				end
				if stat:IsA("IntValue") or stat:IsA("NumberValue") then
					stat.Value = savedValue
				end
			end
		end
	end

	loadedOk[player] = true
	player:SetAttribute("DataLoaded", true) -- other scripts (like checkpoints) can listen for this
end

local function savePlayer(player)
	if not store or not loadedOk[player] then
		return
	end
	local leaderstats = player:FindFirstChild("leaderstats")
	if not leaderstats then
		return
	end

	-- Turn the leaderstats into a simple table like { Coins = 50, Stage = 3 }
	local data = {}
	for _, stat in leaderstats:GetChildren() do
		if stat:IsA("IntValue") or stat:IsA("NumberValue") then
			data[stat.Name] = stat.Value
		end
	end

	local ok, err = tryAFewTimes(function()
		store:UpdateAsync(keyFor(player), function(_oldData)
			return data
		end)
	end)
	if not ok then
		warn("Could not save data for " .. player.Name .. ": " .. tostring(err))
	end
end

Players.PlayerAdded:Connect(loadPlayer)
for _, player in Players:GetPlayers() do
	task.spawn(loadPlayer, player)
end

Players.PlayerRemoving:Connect(function(player)
	savePlayer(player)
	loadedOk[player] = nil
end)

-- Autosave loop
task.spawn(function()
	while true do
		task.wait(AUTOSAVE_SECONDS)
		for _, player in Players:GetPlayers() do
			task.spawn(savePlayer, player)
		end
	end
end)

-- When the server shuts down, save everyone and wait until it's done
game:BindToClose(function()
	local stillSaving = 0
	for _, player in Players:GetPlayers() do
		stillSaving += 1
		task.spawn(function()
			savePlayer(player)
			stillSaving -= 1
		end)
	end

	-- Roblox gives us up to 30 seconds; we wait at most 25
	local startTime = os.clock()
	while stillSaving > 0 and os.clock() - startTime < 25 do
		task.wait(0.5)
	end
end)
