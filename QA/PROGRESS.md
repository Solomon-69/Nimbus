
## 3 Nov, later: glass redesign (Solomon's feedback)
- Mac shell no longer uses NSPopover: a borderless glass panel (frosted NSVisualEffectView, thin white rim) whose shape includes the arrow, so the sky gradient flows into the arrow.
- Top hero removed; today's temperature is now a calm "Now" glass card. All terracotta replaced by sky blue; each widget has its own colour (rain blue + bar, wind teal + direction arrow, UV green-to-red scale, sunrise orange daylight bar, air quality green/amber/red). Quit tile neutral.
- Proof: QA/proof/nimbus-main-glass.png, nimbus-settings.png.

- 3 Nov: arrow-join seam check scripted (QA/arrow-seam-check.sh, light+dark via app theme, silent). 1.0.4 join = one fill, no seam; proof QA/proof/arrow-join-{light,dark}.png. Run it after any panel/arrow change.
