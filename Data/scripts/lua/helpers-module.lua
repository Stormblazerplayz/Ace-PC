--[[
	HELPERS MODULE (handy functions you can use in ALL your scripts)
	A ModuleScript is like a toolbox: you write the code once, and other
	scripts "require" it to use the tools inside.

	HOW TO USE:
	1. In ReplicatedStorage, insert a ModuleScript and name it: Helpers
	2. Paste this in.
	3. In any other Script, use it like this:

		local Helpers = require(game:GetService("ReplicatedStorage"):WaitForChild("Helpers"))

		-- A kill brick in 3 lines:
		Helpers.onTouch(script.Parent, 1, function(player, character, humanoid)
			humanoid.Health = 0
		end)

		-- Give someone 10 coins:
		Helpers.getStat(player, "Coins").Value += 10
]]

local Players = game:GetService("Players")

local Helpers = {}

-- Gives you the character Model that a touching part belongs to (or nil)
function Helpers.getCharacterFromHit(hit)
	local character = hit:FindFirstAncestorOfClass("Model")
	if character and character:FindFirstChildOfClass("Humanoid") then
		return character
	end
	return nil
end

-- Gives you the Player who touched (or nil if it wasn't a player)
function Helpers.getPlayerFromHit(hit)
	local character = Helpers.getCharacterFromHit(hit)
	if character then
		return Players:GetPlayerFromCharacter(character)
	end
	return nil
end

-- Gets (or makes) the leaderstats folder for a player
function Helpers.getLeaderstats(player)
	local leaderstats = player:FindFirstChild("leaderstats")
	if not leaderstats then
		leaderstats = Instance.new("Folder")
		leaderstats.Name = "leaderstats"
		leaderstats.Parent = player
	end
	return leaderstats
end

-- Gets (or makes) a number on the leaderboard, like "Coins" or "Wins"
function Helpers.getStat(player, statName, startValue)
	local leaderstats = Helpers.getLeaderstats(player)
	local stat = leaderstats:FindFirstChild(statName)
	if not stat then
		stat = Instance.new("IntValue")
		stat.Name = statName
		stat.Value = startValue or 0
		stat.Parent = leaderstats
	end
	return stat
end

-- Runs your function when a living PLAYER touches the part,
-- with a cooldown (in seconds) for each player. This is a ready-made debounce!
-- callback gets: (player, character, humanoid)
function Helpers.onTouch(part, cooldown, callback)
	local lastTouch = {}
	return part.Touched:Connect(function(hit)
		local character = Helpers.getCharacterFromHit(hit)
		local player = character and Players:GetPlayerFromCharacter(character)
		if not player then
			return
		end
		local humanoid = character:FindFirstChildOfClass("Humanoid")
		if not humanoid or humanoid.Health <= 0 then
			return
		end
		local now = os.clock()
		if lastTouch[player] and now - lastTouch[player] < cooldown then
			return
		end
		lastTouch[player] = now
		callback(player, character, humanoid)
	end)
end

-- Turns seconds into "M:SS", like 75 -> "1:15" (great for timers)
function Helpers.formatTime(totalSeconds)
	totalSeconds = math.max(0, math.floor(totalSeconds))
	local minutes = math.floor(totalSeconds / 60)
	local seconds = totalSeconds % 60
	return string.format("%d:%02d", minutes, seconds)
end

return Helpers
