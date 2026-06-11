// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.DevTools.Cli.Services;

namespace Microsoft.Agents.A365.DevTools.Cli.Tests.Services;

public class HttpListenerBotCallbackReceiverTests
{
    [Theory]
    [InlineData("Got it..work on it")]
    [InlineData("Got it - working on it...")]
    [InlineData("Working on your request")]
    [InlineData("Thinking...")]
    [InlineData("One moment please")]
    [InlineData("Please wait...")]
    [InlineData("Hold on, let me check")]
    [InlineData("Let me check that for you")]
    [InlineData("Let me look into that")]
    [InlineData("Let me find that information")]
    [InlineData("Let me get that for you")]
    [InlineData("Just a moment...")]
    [InlineData("Just a sec")]
    [InlineData("Looking into it")]
    [InlineData("Processing your request...")]
    public void IsInterimMessage_KnownInterimPatterns_ReturnsTrue(string text)
    {
        HttpListenerBotCallbackReceiver.IsInterimMessage(text)
            .Should().BeTrue(because: "'{0}' is an interim acknowledgment, not a final response", text);
    }

    [Theory]
    [InlineData("Here are your recent emails: 1. Meeting reminder from John...")]
    [InlineData("I found 3 documents matching your search criteria. The first one is about project planning and was created last week.")]
    [InlineData("Processing failed due to an authentication error")]
    [InlineData("I can help you with email, calendar, and file management")]
    [InlineData("Hello! How can I help you today?")]
    [InlineData("The meeting is scheduled for 3 PM tomorrow")]
    [InlineData("Error: Unable to connect to the service")]
    public void IsInterimMessage_RealResponses_ReturnsFalse(string text)
    {
        HttpListenerBotCallbackReceiver.IsInterimMessage(text)
            .Should().BeFalse(because: "'{0}' is a real response, not an interim acknowledgment", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsInterimMessage_NullOrWhitespace_ReturnsFalse(string? text)
    {
        HttpListenerBotCallbackReceiver.IsInterimMessage(text)
            .Should().BeFalse(because: "null/empty text is not an interim message");
    }

    [Fact]
    public void IsInterimMessage_LongTextWithInterimWord_ReturnsFalse()
    {
        var longText = "I'm working on analyzing your data. Here are the preliminary results that I found across multiple sources in your organization's SharePoint sites.";

        HttpListenerBotCallbackReceiver.IsInterimMessage(longText)
            .Should().BeFalse(because: "messages over 60 chars are treated as real responses even if they contain interim-like words");
    }

    [Fact]
    public void IsFinalMessage_TypingActivity_ReturnsFalse()
    {
        var response = new BotCallbackResponse("typing indicator", "typing");

        HttpListenerBotCallbackReceiver.IsFinalMessage(response)
            .Should().BeFalse(because: "typing activities are never final responses");
    }

    [Fact]
    public void IsFinalMessage_MessageWithSubstantiveText_ReturnsTrue()
    {
        var response = new BotCallbackResponse("Here are your recent emails from today", "message");

        HttpListenerBotCallbackReceiver.IsFinalMessage(response)
            .Should().BeTrue(because: "a message with substantive non-interim text is a final response");
    }

    [Fact]
    public void IsFinalMessage_InterimMessage_ReturnsFalse()
    {
        var response = new BotCallbackResponse("Got it..work on it", "message");

        HttpListenerBotCallbackReceiver.IsFinalMessage(response)
            .Should().BeFalse(because: "interim acknowledgment messages are not final responses");
    }

    [Fact]
    public void IsFinalMessage_MessageWithNullText_ReturnsFalse()
    {
        var response = new BotCallbackResponse(null, "message");

        HttpListenerBotCallbackReceiver.IsFinalMessage(response)
            .Should().BeFalse(because: "a message with no text is not a final response");
    }

    [Fact]
    public void IsFinalMessage_NullType_WithText_ReturnsFalse()
    {
        var response = new BotCallbackResponse("some text", null);

        HttpListenerBotCallbackReceiver.IsFinalMessage(response)
            .Should().BeFalse(because: "only message-type activities can be final responses");
    }
}
