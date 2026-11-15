--[[
	STATUS LABEL (shows the round timer on screen)
	Shows the text from the "Status" StringValue in ReplicatedStorage
	(the "round-timer" script makes and updates it).

	HOW TO USE:
	1. In StarterGui, insert a ScreenGui, then a TextLabel inside it.
	   Put the label at the top middle of the screen and make it big.
	2. Insert a LocalScript inside the TextLabel and paste this in.
]]

local ReplicatedStorage = game:GetService("ReplicatedStorage")

local label = script.Parent
local status = ReplicatedStorage:WaitForChild("Status")

-- Make the text easy to read (you can delete these lines and style it yourself)
label.TextScaled = true
label.BackgroundTransparency = 0.3

local function showStatus()
	label.Text = status.Value
end

showStatus()
status.Changed:Connect(showStatus)
