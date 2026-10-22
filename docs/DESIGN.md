# Go Fish: design

The first of Renegade Games' card games, and the one that proves the card kit
([renegade-cards](https://github.com/darren-rawlings/renegade-cards)): once the shuffle, the deal
and the glass hands feel right, every later card game is mostly its rules and its scoring.

## The game
Go Fish for 2 to 5 (you and up to four computer players): seven cards each for two or three
players, five each for four or five (the Bicycle rules); the rest is left face down as the stock.
On your turn, ask for a rank you hold ("Got any sevens?"). If the other player has any, they hand
them all over and you ask again. If not, "Go fish!": draw from the pond; fish the rank you asked
for and you go again, otherwise the turn passes. Four of a kind make a book (the standard; pairs are an option in Settings), laid down in front of
you. A player with no cards draws one at the start of their turn (or sits out once the pond is
empty). When all 13 books are made, the most books wins (13 is odd, so there are no ties).
Rules live in `src/Core/GoFishGame.cs` (tested), which reports each step as an event the table
acts out in order.

## Feel
- **A real deck, really shuffled.** A new game starts with the deck in the middle of the felt: it
  splits, riffles together card by card (each with its own flick of sound), and squares up with
  a knock, twice. Then seven cards each are dealt, one at a time, landing with a slap, and the
  rest slide out into a loose pond.
- **Glass hands.** Each player has two transparent hands: one holds the fan, the other does the
  work, picking up the dealt cards, carrying cards across the table when they're handed over,
  and reaching into the pond to fish. The glass is clear in the middle and bright at the rim, so
  the cards always show through.
- **Your fan, close and readable.** Sorted by rank, every corner index showing. Tap any card to
  ask for its rank; the cards of that rank lift as you ask.
- **Signature effect:** a completed book flies down to your corner of the table with a burst of
  sparks, and at the end the winner's books catch fire (the studio's winner's fire).
- **No spinning.** Card tables stay put: the camera is fixed, and there's no Freeze setting.

## Styles
Two separate menus in Style, shared by every card game:
- **Card backs:** Crimson (a fine lattice), Navy (waves), Emerald (argyle).
- **Card fronts**, each with its own suits and face cards, drawn in code:
  - **Classic:** hearts, diamonds, clubs and spades; a shield (jack), tiara (queen) and crown (king).
  - **Ocean:** starfish (hearts), scallop shells (diamonds), anchors (clubs) and whale tails
    (spades); a ship's wheel (jack), a pearl in its shell (queen) and a trident (king).

## Modes
- You vs the computer: Easy (asks at random), Medium (asks for what it holds most of), Hard (also
  remembers the table: what you've asked for, and what you've said "Go fish!" to).
- **Later: Play nearby** (two phones over Bluetooth or Wi-Fi Direct, via Google Nearby
  Connections, built once in renegade-cards).

## Send
A **Send** button opens the phone's share menu with a link to Go Fish on Google Play, so a friend
can get it and play you. Until the game is on Google Play, it explains that.

## Ads
- Between-games interstitial every 5th finished game.
- Start over against the computer counts as a resignation, unless a rewarded ad is watched.

## Scope for version 1
Go Fish vs the computer with the full table: shuffle, deal, glass hands, books, styles, tutorial,
Send, ads and the shop. **Later:** Play nearby; three or four players (Go Fish deals five each);
tutorial clips recorded from the game; more front and back designs.
