--[[
	WIN PAD (end of an obby)
	Touch it to get +1 Win on the leaderboard, then you get sent back to the start.

	HOW TO USE:
	1. Make a Part at the end of your obby (Anchored). Gold looks cool!
	2. Insert a Script into the Part and paste this in.
	If you also use the "checkpoints" script, RESET_STAGE sends the player back to stage 1.
]]

local Players = game:GetService("Players")

local pad = script.Parent

-- SETTINGS
local RESET_STAGE = true -- true = set their Stage back to 1 (for obbies with checkpoints)
local SEND_BACK_DELAY = 1.5 -- seconds before they get sent back
local COOLDOWN = 5 -- seconds before the same player can win again

pad.Anchored = true

local justWon = {} -- debounce: players who just won

local function getStat(player, statName, startValue)
	local leaderstats = player:FindFirstChild("leaderstats")
	if not leaderstats then
		leaderstats = Instance.new("Folder")
		leaderstats.Name = "leaderstats"
		leaderstats.Parent = player
	end
	local stat = leaderstats:FindFirstChild(statName)
	if not stat then
		stat = Instance.new("IntValue")
		stat.Name = statName
		stat.Value = startValue
		stat.Parent = leaderstats
	end
	return stat
end

pad.Touched:Connect(function(hit)
	local character = hit:FindFirstAncestorOfClass("Model")
	local player = character and Players:GetPlayerFromCharacter(character)
	if not player or justWon[player] then
		return
	end
	local humanoid = character:FindFirstChildOfClass("Humanoid")
	if not humanoid or humanoid.Health <= 0 then
		return
	end

	justWon[player] = true
	getStat(player, "Wins", 0).Value += 1

	if RESET_STAGE then
		local leaderstats = player:FindFirstChild("leaderstats")
		local stage = leaderstats and leaderstats:FindFirstChild("Stage")
		if stage then
			stage.Value = 1
		end
	end

	task.delay(SEND_BACK_DELAY, function()
		if player.Parent == Players then
			player:LoadCharacter() -- respawns them back at the start
		end
	end)

	task.delay(COOLDOWN, function()
		justWon[player] = nil
	end)
end)
