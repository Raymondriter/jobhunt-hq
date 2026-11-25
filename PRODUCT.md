# JobHunt HQ product brief

## Who would use it?

Start with one person like Raymond, a software developer looking for remote work who already uses AI and wants to delegate parts of a search. At the end of a workday they want to know which roles their AI applied for, what it sent, and where a decision from them could move things forward.

Keep JobHunt HQ as the working brand. The customer promise to test is **“Your AI prepares the application. You review the packet and give the final go for each posting.”** The tracker exists today. The preparation and review workflow is planned in [JOBHUNT-5](https://rpr2998.atlassian.net/browse/JOBHUNT-5).

## The first thing to show someone

Tell the story of one application from Raymond's own workflow. Muse is the candidate for the first walkthrough. Establish what Muse does in that example, where Raymond's own AI takes over, and how he knows the application arrived. Use fictional applicant and employer details for the public replay. Label it as a replay and keep the original evidence private.

| Moment in the demo | What the visitor should see |
| --- | --- |
| Choose the work | The applicant sets a target role and location, picks a résumé version, and reviews a specific posting. Approving its packet saves it for the existing assisted submission flow. |
| Explain one match | The agent identifies the posting and quotes the requirement that matched the applicant's experience. The visitor can see why this role qualified. |
| Show what happened | One example ends with a submission confirmation and the résumé version sent. A second stops at a question the applicant must answer. The tracker shows the exact next action for each. |

The existing seed describes 37 submitted applications. It also describes prepared applications that stopped before submission. It does not identify which tool handled each one. Use a single traceable example for any public claim about autonomy.

## First product experiment

Start with one candidate from the existing scan. Show the rule that determined its fit, the résumé version, the prepared answers, and anything Raymond still needs to answer. Support Approve, Edit, and Pass. Approving creates one Saved application. Raymond gives the final go for this posting in the existing assisted submission flow.

Next, let that flow report its outcome to the same account. Include a stable attempt ID and either the confirmation reference or the question that stopped the attempt. Retrying this handoff should update the same attempt without submitting again.

The first release passes when a confirmed attempt appears as Applied, an interrupted attempt surfaces the question without counting as Applied, and replaying either result leaves the totals correct. This gives the demo a working path while keeping the applicant's existing agent useful.

## Practice running the company

| Owner's job | First exercise | Evidence to keep |
| --- | --- | --- |
| Understand the customer | Show the replay to three people currently applying for remote roles. Ask each to show how they checked their last application and where they lost time. | Their own words, the point each would delegate, and whether any volunteer to try it on one role. |
| Own the service | Map who controls the repository, Azure subscription and deployment identity. Rehearse the build, migration and recovery against a local database. Compare the documented configuration with the existing Azure resources. | A second operator can build from source, recover a fictional application, locate a failed deployment, and identify who can roll back a release. |
| Test the business | Offer one volunteer a pilot with their own criteria. Measure minutes they spend per confirmed application, corrections they make, support time, and Azure and AI cost per confirmed application. | Compare with their previous manual application. Ask what outcome would make them return or pay for the next week. |

The first milestone is one outside person who can explain what the agent did, trust the record, and choose to use it again. That person's experience should guide pricing, brand language, and the next integration.

## Release order and operating costs

1. **Prepare the public demo.** Use invented companies, dates and résumé details in public examples. On November 20, 2026, the seed on public GitHub main matched the 37-row personal tracker import. Keep the original in private storage. The [fictional seed pull request](https://github.com/Raymondriter/jobhunt-hq/pull/1) was merged and deployed with four validated examples. Earlier Git history still contains the original. The M3 source was subsequently merged to GitHub main.
2. **Finish the zero-cost portion of M3.** Keep the West US 2 web app on F1 and Azure SQL on its free offer with exhaustion set to AutoPause. Keep reminders on the dashboard during the pilot and rehearse the digest locally with sample data. Resolve the scheduling difference first: Jira specifies 8 a.m. Eastern and a 48-hour window, while the Function uses noon UTC and includes overdue items plus the next seven days. Leave hosted email as a separately costed option. Limit uploaded bytes per user as well as per file.
3. **Review spend weekly and after releases.** Record reported spend, free SQL allowance remaining, telemetry volume and stored bytes. The subscription budget on November 20 reported USD 0 against USD 5. Cost data can lag. On November 20, the daily caps on both Log Analytics and Application Insights were set to 0.05 GB and verified in Azure. Review sampling once the workspace reports representative traffic. Allow for overshoot and for monitoring gaps when capped. [Azure Monitor pricing](https://azure.microsoft.com/en-us/pricing/details/monitor/) · [Daily cap behavior](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/daily-cap)
4. **Pilot one M4 workflow on existing infrastructure.** Use existing scan output and local AI. Check a clean fit and a hard rejection, approve one packet, and replay its import. One account should see one Saved record until a submission confirmation arrives. A second account should only see its own records. As a separate import test, importing the same 37-row CSV twice should add zero rows on the second pass while preserving genuinely separate postings.
5. **Test what someone would buy.** Keep basic tracking and in-app reminders free during the pilot. Offer a small, manually supported pilot for tailored packets and review history once someone asks to use the workflow again. Record preparation time saved, corrections, support minutes and variable cost per reviewed packet. Build billing or hosted AI only after a pilot customer agrees to a price that covers those costs.
