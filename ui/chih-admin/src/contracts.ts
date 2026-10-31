import { z } from 'zod';

export const prioritySchema = z.enum(['Low', 'Normal', 'High']);
export const categorySchema = z.enum(['Personal', 'Study', 'Work']);
// A calendar date crosses the bridge as YYYY-MM-DD, never as a JS Date or UTC midnight.
export const dateSchema = z.iso.date();
export const taskInputSchema = z.strictObject({
  title: z.string().trim().min(1, 'Введите название').max(200, 'Не более 200 символов'),
  description: z.string().nullable(), priority: prioritySchema, category: categorySchema,
  dueDate: dateSchema.nullable(),
});
export const taskSchema = taskInputSchema.extend({
  id: z.uuid(), status: z.enum(['Active', 'Completed']),
  createdAtUtc: z.iso.datetime({ offset: true }), updatedAtUtc: z.iso.datetime({ offset: true }),
  completedAtUtc: z.iso.datetime({ offset: true }).nullable(), revision: z.number().int().positive(),
});
export const viewSchema = z.enum(['Inbox', 'Today', 'Upcoming', 'Completed', 'AllActive']);
export const querySchema = z.strictObject({
  view: viewSchema, sort: z.enum(['DueDate', 'Priority', 'CreatedAt']), direction: z.enum(['Ascending', 'Descending']),
});
export const payloadSchemas = {
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
