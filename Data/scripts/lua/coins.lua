--[[
	COLLECTABLE COINS + LEADERSTATS
	Makes a "Coins" score on the player list, and every coin part you touch adds to it.
	Coins come back after a while so people can collect them again.

	HOW TO USE:
	1. In Workspace, insert a Folder named: Coins
	2. Put your coin Parts inside it (a yellow Cylinder makes a nice coin).
	   Optional: give a coin an Attribute called "Value" (a number) to make it worth more.
	   Optional: put a Sound inside a coin and it will play when collected.
	3. Put this Script in ServerScriptService.
]]

local Players = game:GetService("Players")

local coinsFolder = workspace:WaitForChild("Coins")

-- SETTINGS
local DEFAULT_COIN_VALUE = 1 -- coins you get if the coin has no "Value" attribute
local RESPAWN_TIME = 10 -- seconds until a coin comes back (set to 0 to make coins disappear forever)

-- Make the player's leaderstats folder and Coins number (if they don't have them yet)
local function getCoinsValue(player)
	local leaderstats = player:FindFirstChild("leaderstats")
	if not leaderstats then
		leaderstats = Instance.new("Folder")
		leaderstats.Name = "leaderstats"
		leaderstats.Parent = player
	end

	local coins = leaderstats:FindFirstChild("Coins")
	if not coins then
		coins = Instance.new("IntValue")
		coins.Name = "Coins"
		coins.Value = 0
		coins.Parent = leaderstats
	end
	return coins
end

Players.PlayerAdded:Connect(getCoinsValue)
for _, player in Players:GetPlayers() do
	getCoinsValue(player)
end

local function setUpCoin(coin)
	if not coin:IsA("BasePart") then
		return
	end

	coin.Anchored = true
	coin.CanCollide = false -- you walk through coins instead of bumping into them

	local normalTransparency = coin.Transparency

	coin.Touched:Connect(function(hit)
		-- Debounce: a coin that was just collected can't be collected again
		if coin:GetAttribute("Collected") then
			return
		end

		local character = hit:FindFirstAncestorOfClass("Model")
		local player = character and Players:GetPlayerFromCharacter(character)
		if not player then
			return
		end
		local humanoid = character:FindFirstChildOfClass("Humanoid")
		if not humanoid or humanoid.Health <= 0 then
			return
		end

		coin:SetAttribute("Collected", true)

		local value = coin:GetAttribute("Value")
		if type(value) ~= "number" then
			value = DEFAULT_COIN_VALUE
		end
		getCoinsValue(player).Value += value

		local sound = coin:FindFirstChildOfClass("Sound")
		if sound then
			sound:Play()
		end

		-- Hide the coin
		coin.Transparency = 1

		if RESPAWN_TIME > 0 then
			task.delay(RESPAWN_TIME, function()
				coin.Transparency = normalTransparency
				coin:SetAttribute("Collected", nil)
			end)
		else
			task.delay(1, function()
				coin:Destroy() -- wait 1 second first so the sound can finish
			end)
		end
	end)
end

for _, object in coinsFolder:GetDescendants() do
	setUpCoin(object)
end
coinsFolder.DescendantAdded:Connect(setUpCoin)
