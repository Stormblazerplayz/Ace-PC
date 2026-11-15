--[[
	SHOP (SERVER PART) - use together with the "shop-button" LocalScript
	The server is the boss: it checks the price and takes the coins, so hackers
	can't give themselves free items.

	HOW TO USE:
	1. In ServerStorage, insert a Folder named: ShopItems
	2. Put the Tools you want to sell inside it (for example a Tool named Sword).
	3. Write each tool's exact name and price in the PRICES list below.
	4. Put this Script in ServerScriptService.
	It creates a RemoteEvent called "BuyItem" in ReplicatedStorage for you.
	Players need a "Coins" leaderstat (the "coins" script makes one).
]]

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local ServerStorage = game:GetService("ServerStorage")

-- SETTINGS: tool name = price (names must match the tools in ServerStorage.ShopItems)
local PRICES = {
	Sword = 50,
	SpeedCoil = 100,
}
local KEEP_AFTER_DEATH = true -- true = you keep bought items after you respawn

local shopItems = ServerStorage:WaitForChild("ShopItems")

-- Make the RemoteEvent the buttons will use
local buyEvent = ReplicatedStorage:FindFirstChild("BuyItem")
if not buyEvent then
	buyEvent = Instance.new("RemoteEvent")
	buyEvent.Name = "BuyItem"
	buyEvent.Parent = ReplicatedStorage
end

local function getCoins(player)
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
		coins.Parent = leaderstats
	end
	return coins
end

local function alreadyHas(player, itemName)
	local backpack = player:FindFirstChildOfClass("Backpack")
	local character = player.Character
	return (backpack and backpack:FindFirstChild(itemName) ~= nil)
		or (character and character:FindFirstChild(itemName) ~= nil)
end

local lastBuy = {} -- debounce so people can't spam the button

buyEvent.OnServerEvent:Connect(function(player, itemName)
	-- Never trust what the client sends! Check everything.
	if typeof(itemName) ~= "string" then
		return
	end

	local now = os.clock()
	if lastBuy[player] and now - lastBuy[player] < 0.5 then
		return
	end
	lastBuy[player] = now

	local price = PRICES[itemName]
	local template = shopItems:FindFirstChild(itemName)
	if not price or not template then
		buyEvent:FireClient(player, itemName, false, "Not for sale")
		return
	end

	if alreadyHas(player, itemName) then
		buyEvent:FireClient(player, itemName, false, "You have it!")
		return
	end

	local coins = getCoins(player)
	if coins.Value < price then
		buyEvent:FireClient(player, itemName, false, "Need " .. (price - coins.Value) .. " more")
		return
	end

	-- Take the coins and give the item
	coins.Value -= price

	local backpack = player:FindFirstChildOfClass("Backpack")
	if backpack then
		template:Clone().Parent = backpack
	end
	if KEEP_AFTER_DEATH then
		local starterGear = player:FindFirstChild("StarterGear")
		if starterGear and not starterGear:FindFirstChild(itemName) then
			template:Clone().Parent = starterGear
		end
	end

	buyEvent:FireClient(player, itemName, true, "Bought!")
end)

Players.PlayerRemoving:Connect(function(player)
	lastBuy[player] = nil
end)
