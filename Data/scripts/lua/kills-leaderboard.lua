--[[
	KILLS + DEATHS LEADERBOARD
	Adds "Kills" and "Deaths" to the player list.
	You get a kill when someone you hit (with a weapon that leaves a "creator" tag,
	like the "sword-tool" script) dies.

	HOW TO USE:
	1. Put this Script in ServerScriptService.
	2. Use weapons that add an ObjectValue named "creator" (Value = the attacker Player)
	   inside the victim's Humanoid. The sword-tool script already does this.
]]

local Players = game:GetService("Players")

local function getStat(player, statName)
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
		stat.Parent = leaderstats
	end
	return stat
end

local function onCharacterAdded(player, character)
	local humanoid = character:WaitForChild("Humanoid")

	humanoid.Died:Connect(function()
		getStat(player, "Deaths").Value += 1

		-- Who hit them last?
		local tag = humanoid:FindFirstChild("creator")
		if tag and tag:IsA("ObjectValue") then
			local killer = tag.Value
			if killer and killer:IsA("Player") and killer ~= player and killer.Parent == Players then
				getStat(killer, "Kills").Value += 1
			end
		end
	end)
end

local function onPlayerAdded(player)
	getStat(player, "Kills")
	getStat(player, "Deaths")

	player.CharacterAdded:Connect(function(character)
		onCharacterAdded(player, character)
	end)
	if player.Character then
		task.spawn(onCharacterAdded, player, player.Character)
	end
end

Players.PlayerAdded:Connect(onPlayerAdded)
for _, player in Players:GetPlayers() do
	onPlayerAdded(player)
end
