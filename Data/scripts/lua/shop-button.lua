--[[
	SHOP BUTTON (CLIENT PART) - use together with the "shop-server" Script
	When you click the button, it asks the server to buy the item.
	The button then shows "Bought!" or why it didn't work.

	HOW TO USE:
	1. In StarterGui, insert a ScreenGui, then a TextButton inside it.
	2. Name the TextButton the SAME as the tool you sell (for example: Sword).
	   (Or give the button an Attribute "ItemName" with the tool's name.)
	3. Insert a LocalScript inside the TextButton and paste this in.
	Make one button (with this LocalScript) for each item in your shop.
]]

local ReplicatedStorage = game:GetService("ReplicatedStorage")

local button = script.Parent
local itemName = button:GetAttribute("ItemName") or button.Name

local buyEvent = ReplicatedStorage:WaitForChild("BuyItem")

local normalText = button.Text
local normalColor = button.BackgroundColor3
local busy = false -- debounce so one click = one buy request
local messageNumber = 0

button.Activated:Connect(function()
	if busy then
		return
	end
	busy = true
	buyEvent:FireServer(itemName)
	task.delay(0.6, function()
		busy = false
	end)
end)

-- The server answers: which item, did it work, and a short message
buyEvent.OnClientEvent:Connect(function(boughtItem, success, message)
	if boughtItem ~= itemName then
		return -- that answer was for a different button
	end

	messageNumber += 1
	local myMessage = messageNumber

	button.Text = tostring(message)
	if success then
		button.BackgroundColor3 = Color3.fromRGB(70, 200, 90) -- green
	else
		button.BackgroundColor3 = Color3.fromRGB(220, 70, 70) -- red
	end

	task.wait(1.5)
	if myMessage == messageNumber then -- only reset if no newer message showed up
		button.Text = normalText
		button.BackgroundColor3 = normalColor
	end
end)
