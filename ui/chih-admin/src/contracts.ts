import { z } from 'zod';

export const prioritySchema = z.enum(['Low', 'Normal', 'High']);
export const categorySchema = z.enum(['Personal', 'Study', 'Work']);
// A calendar date crosses the bridge as YYYY-MM-DD, never as a JS Date or UTC midnight.
export const dateSchema = z.iso.date();
export const taskInputSchema = z.strictObject({
  title: z.string().trim().min(1, 'Введите название').max(200, 'Не более 200 символов'),
  description: z.string().nullable(), priority: prioritySchema, category: categorySchema,
  dueDate: dateSchema.nullable(), remindAtUtc: z.iso.datetime({ offset: true }).nullable().optional(),
});
export const taskSchema = taskInputSchema.extend({
  id: z.uuid(), status: z.enum(['Active', 'Completed']),
  createdAtUtc: z.iso.datetime({ offset: true }), updatedAtUtc: z.iso.datetime({ offset: true }),
  reminderStatus: z.enum(["Disabled", "Pending", "Delivered", "Dismissed"]).optional(), snoozedUntilUtc: z.iso.datetime({ offset: true }).nullable().optional(), deliveredAtUtc: z.iso.datetime({ offset: true }).nullable().optional(),
  completedAtUtc: z.iso.datetime({ offset: true }).nullable(), revision: z.number().int().positive(),
});
export const viewSchema = z.enum(['Inbox', 'Today', 'Upcoming', 'Completed', 'AllActive']);
export const querySchema = z.strictObject({
  view: viewSchema, sort: z.enum(['DueDate', 'Priority', 'CreatedAt']), direction: z.enum(['Ascending', 'Descending']),
});
const timeSchema = z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/);
const identity = z.string().min(1).max(200);
const version = z.string().regex(/^[0-9A-F]{64}$/);
export const reminderInputSchema = z.strictObject({
  title: z.string().trim().min(1).max(200), time: timeSchema,
  recurrence: z.enum(['Daily', 'Weekly', 'RunningInterval']), intervalMinutes: z.number().int().min(1).max(1440),
  weeklyDay: z.enum(['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']),
  enabled: z.boolean(), bypassQuietHours: z.boolean(),
});
export const reminderSchema = z.strictObject({ id: identity, version, definition: reminderInputSchema, timeZoneId: z.string() });
export const remindersSchema = z.strictObject({
  definitions: z.array(reminderSchema), occurrences: z.array(z.strictObject({ id: identity, reminderId: identity, title: z.string(),
    localDate: dateSchema, dueAtUtc: z.iso.datetime({ offset: true }), acknowledgment: z.enum(['Pending', 'Dismissed', 'Completed']),
    snoozedUntilUtc: z.iso.datetime({ offset: true }).nullable(), isManualTest: z.boolean() })),
  truncated: z.boolean(), baseTimeZone: z.string(), error: z.string().nullable(),
});
export const settingsInputSchema = z.strictObject({
  ownerDisplayName: z.string().max(60), timeZoneId: z.string().min(1), launchAtSignIn: z.boolean(), startPetOnly: z.boolean(), petTopmost: z.boolean(),
  taskReminderNotificationsEnabled: z.boolean().default(true), taskSummarySpeechEnabled: z.boolean().default(false), showTaskTitlesInBubbles: z.boolean().default(true), taskCompletionReactionsEnabled: z.boolean().default(true),
  reminderNotificationsEnabled: z.boolean(), personalityEnabled: z.boolean(), spontaneousCommentsEnabled: z.boolean(),
  minimumCommentIntervalMinutes: z.number().int().min(1).max(1440), quietHoursEnabled: z.boolean(), quietStart: timeSchema, quietEnd: timeSchema,
});
export const settingsSchema = z.strictObject({ settings: settingsInputSchema, version, canWrite: z.boolean(), error: z.string().nullable(),
  timeZones: z.array(z.strictObject({ id: z.string(), label: z.string() })) });
export type ReminderInput = z.infer<typeof reminderInputSchema>;
export type Reminder = z.infer<typeof reminderSchema>;
export type Reminders = z.infer<typeof remindersSchema>;
export type SettingsInput = z.infer<typeof settingsInputSchema>;
export type Settings = z.infer<typeof settingsSchema>;
export const payloadSchemas = {
  'planner.resolveTime': z.strictObject({ local: z.string().regex(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/), timeZoneId: z.string().min(1) }),
  'planner.reminder': z.strictObject({ id: z.uuid(), action: z.enum(['snooze', 'dismiss']), expectedRevision: z.number().int().positive() }),
  'reminders.list': z.strictObject({}),
  'reminders.create': z.strictObject({ reminder: reminderInputSchema }),
  'reminders.update': z.strictObject({ id: identity, expectedVersion: version, reminder: reminderInputSchema }),
  'reminders.enable': z.strictObject({ id: identity, expectedVersion: version, enabled: z.boolean() }),
  'reminders.delete': z.strictObject({ id: identity, expectedVersion: version }),
  'reminders.snooze': z.strictObject({ id: identity }),
  'reminders.acknowledge': z.strictObject({ id: identity, completed: z.boolean() }),
  'settings.get': z.strictObject({}),
  'settings.save': z.strictObject({ expectedVersion: version, settings: settingsInputSchema }),
  'settings.open': z.strictObject({}),
  'planner.list': z.strictObject({ query: querySchema }),
  'planner.get': z.strictObject({ id: z.uuid() }),
  'planner.create': z.strictObject({ task: taskInputSchema }),
  'planner.update': z.strictObject({ id: z.uuid(), task: taskInputSchema, expectedRevision: z.number().int().positive() }),
  'planner.complete': z.strictObject({ id: z.uuid() }),
  'planner.reopen': z.strictObject({ id: z.uuid() }),
  'planner.delete': z.strictObject({ id: z.uuid(), expectedRevision: z.number().int().positive() }),
} as const;
export type Operation = keyof typeof payloadSchemas;
export type Payload<O extends Operation> = z.infer<(typeof payloadSchemas)[O]>;
export const responseSchemas = {
  'planner.resolveTime': z.iso.datetime({ offset: true }), 'planner.reminder': taskSchema,
  'reminders.list': remindersSchema, 'reminders.create': reminderSchema, 'reminders.update': reminderSchema,
  'reminders.enable': z.boolean(), 'reminders.delete': z.boolean(), 'reminders.snooze': z.boolean(), 'reminders.acknowledge': z.boolean(),
  'settings.get': settingsSchema, 'settings.save': settingsSchema,
  'settings.open': z.boolean(),
  'planner.list': z.array(taskSchema), 'planner.get': taskSchema, 'planner.create': taskSchema,
  'planner.update': taskSchema, 'planner.complete': taskSchema, 'planner.reopen': taskSchema,
  'planner.delete': z.boolean(),
} as const;
export type Result<O extends Operation> = z.infer<(typeof responseSchemas)[O]>;
export const responseSchema = z.strictObject({
  protocolVersion: z.literal(1), requestId: z.uuid().nullable(), operation: z.string(),
  success: z.boolean(), data: z.unknown(), error: z.strictObject({ code: z.string(), message: z.string() }).nullable(),
}).superRefine((response, context) => {
  if (response.success ? response.error !== null : response.error === null || response.data !== null)
    context.addIssue({ code: 'custom', message: 'Invalid success/error envelope' });
});
export const requestSchema = z.strictObject({
  protocolVersion: z.literal(1), requestId: z.uuid(), operation: z.enum(Object.keys(payloadSchemas) as [Operation, ...Operation[]]), payload: z.unknown(),
}).superRefine((request, context) => {
  if (!payloadSchemas[request.operation].safeParse(request.payload).success)
    context.addIssue({ code: 'custom', message: 'Invalid operation payload' });
});
export type PlannerTask = z.infer<typeof taskSchema>;
export type TaskInput = z.infer<typeof taskInputSchema>;
export type Query = z.infer<typeof querySchema>;
export type View = z.infer<typeof viewSchema>;
